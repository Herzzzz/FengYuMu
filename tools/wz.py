"""最小 WZ (PKG1) 解析器 —— 只实现读 String.wz 需要的部分。

不支持 Canvas/Sound 的实际数据解码(String.wz 里没有),遇到时跳过。
版本号和字符串加密 IV 都自动探测,不需要人工指定。

用法:
    python tools/wz.py info  .wz/CW_Ost1/Data/String/String_000.wz
    python tools/wz.py dump  .wz/CW_Ost1/Data/String/String_000.wz Skill.img --limit 5
"""
from __future__ import annotations

import io
import struct
import sys
from pathlib import Path

from Crypto.Cipher import AES

# WZ 通用 AES-256 密钥(所有客户端相同,取自 UserKey 的每第 4 字节)
AES_KEY = bytes([
    0x13, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00,
    0x06, 0x00, 0x00, 0x00, 0xB4, 0x00, 0x00, 0x00,
    0x1B, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00, 0x00,
    0x33, 0x00, 0x00, 0x00, 0x52, 0x00, 0x00, 0x00,
])

# 三种已知的字符串加密 IV。NONE 表示不加密(部分老客户端 / BMS)。
IVS = {
    "GMS": bytes([0x4D, 0x23, 0xC7, 0x2B]) * 4,
    "MS": bytes([0xB9, 0x7D, 0x63, 0xE9]) * 4,   # KMS / CMS / MSEA
    "NONE": None,
}

OFFSET_CONSTANT = 0x581C3F6D


class WzKey:
    """按需延长的 AES-OFB 密钥流。IV 为 None 时退化成全 0(即不加密)。"""

    def __init__(self, iv: bytes | None):
        self._iv = iv
        self._buf = b""

    def _grow(self, n: int) -> None:
        if self._iv is None:
            self._buf = b"\x00" * n
            return
        cipher = AES.new(AES_KEY, AES.MODE_ECB)
        block = self._buf[-16:] if self._buf else self._iv
        out = [self._buf]
        made = len(self._buf)
        while made < n:
            block = cipher.encrypt(block)
            out.append(block)
            made += 16
        self._buf = b"".join(out)

    def __getitem__(self, i: int) -> int:
        if i >= len(self._buf):
            self._grow(i + 1024)
        return self._buf[i]


class WzReader:
    """带 WZ 特有读法(压缩整数、加密字符串、加密偏移)的二进制游标。"""

    def __init__(self, data: bytes, fstart: int, version_hash: int, key: WzKey):
        self.s = io.BytesIO(data)
        self.data = data
        self.fstart = fstart
        self.version_hash = version_hash
        self.key = key

    # --- 基本类型 ---
    def tell(self) -> int:
        return self.s.tell()

    def seek(self, pos: int) -> None:
        self.s.seek(pos)

    def u8(self) -> int:
        return self.s.read(1)[0]

    def i8(self) -> int:
        return struct.unpack("<b", self.s.read(1))[0]

    def u16(self) -> int:
        return struct.unpack("<H", self.s.read(2))[0]

    def i16(self) -> int:
        return struct.unpack("<h", self.s.read(2))[0]

    def u32(self) -> int:
        return struct.unpack("<I", self.s.read(4))[0]

    def i32(self) -> int:
        return struct.unpack("<i", self.s.read(4))[0]

    def i64(self) -> int:
        return struct.unpack("<q", self.s.read(8))[0]

    def f32(self) -> float:
        return struct.unpack("<f", self.s.read(4))[0]

    def f64(self) -> float:
        return struct.unpack("<d", self.s.read(8))[0]

    def compressed_int(self) -> int:
        b = self.i8()
        return self.i32() if b == -128 else b

    def compressed_long(self) -> int:
        b = self.i8()
        return self.i64() if b == -128 else b

    # --- WZ 特有 ---
    def string(self) -> str:
        """加密字符串。正长度 = UTF-16LE,负长度 = ASCII。"""
        n = self.i8()
        if n == 0:
            return ""
        if n > 0:  # Unicode
            length = self.i32() if n == 127 else n
            if length <= 0:
                return ""
            raw = self.s.read(length * 2)
            mask = 0xAAAA
            out = bytearray()
            for i in range(length):
                lo = raw[i * 2] ^ (mask & 0xFF) ^ self.key[i * 2]
                hi = raw[i * 2 + 1] ^ (mask >> 8) ^ self.key[i * 2 + 1]
                out += bytes((lo, hi))
                mask = (mask + 1) & 0xFFFF
            return out.decode("utf-16-le", errors="replace")
        # ASCII
        length = self.i32() if n == -128 else -n
        if length <= 0:
            return ""
        raw = self.s.read(length)
        mask = 0xAA
        out = bytearray()
        for i in range(length):
            out.append(raw[i] ^ mask ^ self.key[i])
            mask = (mask + 1) & 0xFF
        return out.decode("latin-1", errors="replace")

    def string_at(self, pos: int) -> str:
        keep = self.tell()
        self.seek(pos)
        v = self.string()
        self.seek(keep)
        return v

    def string_block(self, image_offset: int) -> str:
        """.img 内部的字符串:可能内联,也可能是指向去重池的偏移。"""
        t = self.u8()
        if t in (0x00, 0x73):
            return self.string()
        if t in (0x01, 0x1B):
            return self.string_at(image_offset + self.i32())
        raise ValueError(f"未知 string_block 类型 0x{t:02X} @ {self.tell()}")

    def offset(self) -> int:
        """目录项里的加密偏移。"""
        v = (~(self.tell() - self.fstart)) & 0xFFFFFFFF
        v = (v * self.version_hash) & 0xFFFFFFFF
        v = (v - OFFSET_CONSTANT) & 0xFFFFFFFF
        n = v & 0x1F
        v = ((v << n) | (v >> (32 - n))) & 0xFFFFFFFF  # 循环左移
        v ^= self.u32()
        return (v + self.fstart * 2) & 0xFFFFFFFF


def version_hash(version: int) -> tuple[int, int]:
    """返回 (32 位版本哈希, 文件头里那个 16 位校验值)。"""
    h = 0
    for c in str(version):
        h = (h * 32 + ord(c) + 1) & 0xFFFFFFFF
    check = 0xFF
    for shift in (24, 16, 8, 0):
        check ^= (h >> shift) & 0xFF
    return h, check


class WzFile:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        self.data = self.path.read_bytes()
        if self.data[:4] != b"PKG1":
            raise ValueError(f"{self.path} 不是 PKG1 格式的 WZ 文件")
        self.fsize = struct.unpack_from("<Q", self.data, 4)[0]
        self.fstart = struct.unpack_from("<I", self.data, 12)[0]
        self.enc_ver = struct.unpack_from("<H", self.data, self.fstart)[0]
        self.version = None
        self.iv_name = None
        self._detect()

    # --- 版本 / IV 探测 ---
    def _detect(self) -> None:
        candidates = [v for v in range(1, 3000) if version_hash(v)[1] == self.enc_ver]
        for iv_name, iv in IVS.items():
            for v in candidates:
                vh = version_hash(v)[0]
                try:
                    entries = self._read_root(vh, WzKey(iv))
                except Exception:
                    continue
                if self._looks_sane(entries) and self._offsets_in_range(entries):
                    self.version, self.iv_name = v, iv_name
                    self.root = entries
                    return
        raise ValueError(
            f"{self.path.name}: 版本/加密探测失败 "
            f"(encVer=0x{self.enc_ver:04X},候选版本 {len(candidates)} 个)"
        )

    def _offsets_in_range(self, entries: list[dict]) -> bool:
        """每个 img 的数据必须真的落在文件里。

        **光看名字正常是不够的**:目录项里只有 offset 这一个字段用 version_hash 解密,
        img 名和 size 都不依赖它。而头里那个 encVer 只是个 8 位校验,几十个版本号都能对上,
        于是会挑中一个「名字读得出来、offset 全是垃圾」的版本 —— 直到 read_image
        才以 IndexError 炸掉,而且看不出根因。

        踩这个坑的是 KinokoMS(GMS v95)的 wz:探测选了 version=16,
        解出来的 offset 是 6.4 亿,而文件才 374 万。
        """
        limit = len(self.data)
        for e in entries:
            if e["type"] != "img":
                continue
            off, size = e["offset"], e["size"]
            if not (self.fstart <= off < limit) or off + size > limit:
                return False
        return True

    @staticmethod
    def _looks_sane(entries: list[dict]) -> bool:
        """根目录里应该有一批名字正常、以 .img 结尾的条目。"""
        if not entries:
            return False
        imgs = [e for e in entries if e["type"] == "img"]
        if len(imgs) < max(1, len(entries) // 2):
            return False
        for e in entries:
            name = e["name"]
            if not name or not all(32 <= ord(c) < 127 for c in name):
                return False
            if e["type"] == "img" and not name.endswith(".img"):
                return False
        return True

    def _read_root(self, vh: int, key: WzKey) -> list[dict]:
        r = WzReader(self.data, self.fstart, vh, key)
        r.seek(self.fstart + 2)
        return self._read_dir(r)

    def _read_dir(self, r: WzReader) -> list[dict]:
        count = r.compressed_int()
        if not (0 < count < 5000):
            raise ValueError(f"目录条目数不合理: {count}")
        out = []
        for _ in range(count):
            t = r.u8()
            if t == 1:  # 未使用的占位项
                r.s.read(10)
                continue
            if t == 2:  # 名字存在别处
                pos = self.fstart + r.i32()
                keep = r.tell()
                r.seek(pos)
                t = r.u8()
                name = r.string()
                r.seek(keep)
            elif t in (3, 4):
                name = r.string()
            else:
                raise ValueError(f"未知目录项类型 {t}")
            size = r.compressed_int()
            r.compressed_int()  # checksum,不校验
            off = r.offset()
            out.append({
                "name": name,
                "type": "dir" if t == 3 else "img",
                "size": size,
                "offset": off,
            })
        return out

    # --- 读取 .img ---
    def reader(self) -> WzReader:
        return WzReader(self.data, self.fstart, version_hash(self.version)[0],
                        WzKey(IVS[self.iv_name]))

    def list_images(self) -> list[str]:
        return [e["name"] for e in self.root if e["type"] == "img"]

    def read_image(self, name: str) -> dict:
        entry = next((e for e in self.root if e["name"] == name), None)
        if entry is None:
            raise KeyError(f"{self.path.name} 里没有 {name}(有:{self.list_images()})")
        r = self.reader()
        base = entry["offset"]
        r.seek(base)
        marker = r.u8()
        if marker != 0x73:
            raise ValueError(f"{name}: 期望 0x73,实际 0x{marker:02X}")
        kind = r.string()
        if kind != "Property":
            raise ValueError(f"{name}: 期望 Property,实际 {kind!r}")
        r.u16()
        return _read_props(r, base)


def _read_props(r: WzReader, base: int) -> dict:
    out = {}
    for _ in range(r.compressed_int()):
        name = r.string_block(base)
        out[name] = _read_value(r, base)
    return out


def _read_value(r: WzReader, base: int):
    t = r.u8()
    if t == 0x00:
        return None
    if t in (0x02, 0x0B):
        return r.i16()
    if t in (0x03, 0x13):
        return r.compressed_int()
    if t == 0x04:
        return r.f32() if r.u8() == 0x80 else 0.0
    if t == 0x05:
        return r.f64()
    if t == 0x08:
        return r.string_block(base)
    if t == 0x14:
        return r.compressed_long()
    if t == 0x09:
        end = r.u32() + r.tell()
        v = _read_extended(r, base)
        r.seek(end)
        return v
    raise ValueError(f"未知属性类型 0x{t:02X} @ {r.tell()}")


def _read_extended(r: WzReader, base: int):
    kind = r.string_block(base)
    if kind in ("Property", "Shape2D#Convex2D"):
        r.u16()
        return _read_props(r, base)
    if kind == "Shape2D#Vector2D":
        return (r.compressed_int(), r.compressed_int())
    if kind == "UOL":
        r.u8()
        return r.string_block(base)
    # Canvas / Sound_DX8 等:String.wz 里没有,跳过即可
    return f"<{kind}>"


def _cli() -> None:
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    cmd, path = sys.argv[1], sys.argv[2]
    wz = WzFile(path)
    print(f"{wz.path.name}: version={wz.version} iv={wz.iv_name} "
          f"encVer=0x{wz.enc_ver:04X} fstart={wz.fstart}")
    if cmd == "info":
        for e in wz.root:
            print(f"  [{e['type']}] {e['name']:<24} size={e['size']}")
    elif cmd == "dump":
        img = sys.argv[3]
        limit = int(sys.argv[sys.argv.index("--limit") + 1]) if "--limit" in sys.argv else 3
        props = wz.read_image(img)
        print(f"  {img}: {len(props)} 个顶层条目")
        for k, v in list(props.items())[:limit]:
            print(f"  --- {k} ---")
            print(f"  {v}")
    else:
        print(f"未知命令 {cmd}")
        sys.exit(1)


if __name__ == "__main__":
    _cli()
