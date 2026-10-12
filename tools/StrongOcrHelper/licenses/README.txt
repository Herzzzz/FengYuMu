Strong OCR helper — third-party components
==========================================

All SimdPaddleOCR components below are Apache-2.0 (taken from each NuGet package .nuspec,
2026-10-09):

| package                                        | version | license    | project                               |
|------------------------------------------------|---------|------------|---------------------------------------|
| Sdcb.SimdPaddleOCR                             | 2.0.0   | Apache-2.0 | https://github.com/sdcb/SimdPaddleOCR |
| Sdcb.SimdPaddleOCR.ModelProvider               | 1.0.0   | Apache-2.0 | https://github.com/sdcb/SimdPaddleOCR |
| Sdcb.SimdPaddleOCR.Models.ChineseV6Small       | 1.0.0   | Apache-2.0 | https://github.com/sdcb/SimdPaddleOCR |
| Sdcb.SimdPaddleOCR.Models.TextLineOrientation  | 1.0.0   | Apache-2.0 | https://github.com/sdcb/SimdPaddleOCR |
| Microsoft.Bcl.Numerics                         | 10.0.11 | MIT        | https://github.com/dotnet/runtime     |

Notes
-----
- The upstream LICENSE was fetched from the official repository (HTTP 200, 11,367 bytes, first line
  "Apache License") and is stored beside this file as APACHE-2.0-LICENSE.txt.
- The upstream repository has no THIRD-PARTY-NOTICES file on master (HTTP 404 for both
  THIRD-PARTY-NOTICES and THIRD-PARTY-NOTICES.txt).
- Microsoft.Bcl.Numerics ships its own THIRD-PARTY-NOTICES.TXT inside the NuGet package; a copy is
  stored here as MICROSOFT-BCL-NUMERICS-THIRD-PARTY-NOTICES.TXT if that file was present when the
  helper was packaged.
- This file records exactly which license files are present in this directory. It is not a legal
  review, and it must not be described as a complete licence audit.
