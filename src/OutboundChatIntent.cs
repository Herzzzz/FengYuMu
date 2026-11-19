using System;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal enum OutboundChatIntentKind
    {
        General,
        PartyJoin,
        PartyRecruit,
        Buy,
        Sell,
        Trade,
        PriceCheck,
        RequestHelp,
        OfferHelp
    }

    internal sealed class OutboundChatIntent
    {
        internal static readonly OutboundChatIntent General =
            new OutboundChatIntent(OutboundChatIntentKind.General);

        internal readonly OutboundChatIntentKind Kind;

        internal OutboundChatIntent(OutboundChatIntentKind kind)
        {
            Kind = kind;
        }
    }

    // Pure policy kept outside the WinForms class so player intent can be tested without
    // starting OCR, the online client, or the chat window.  It deliberately classifies the
    // speaker's role instead of keying on a particular map, quest, or screenshot sentence.
    internal static class OutboundChatIntentPolicy
    {
        private const string PartyRoleBody =
            "远程|近战|输出|dps|打手|坦克|奶妈?|牧师|祭司|主教|战士|剑客|准骑士|" +
            "枪战士|龙骑士|法师|魔法师|火毒(?:法师)?|冰雷(?:法师)?|弓手|弓箭手|猎人|" +
            "弩手|弩弓手|飞侠|标飞|刀飞|刺客|侠客|拳手|枪手";
        private const string PartyRole = "(?:" + PartyRoleBody + ")";

        internal static OutboundChatIntent Analyze(string source)
        {
            string line = (source ?? "").Trim();
            if (line.Length == 0) return OutboundChatIntent.General;
            string compact = Regex.Replace(line.ToLowerInvariant(),
                @"[\s，,。.!！?？、：:；;]+", "");

            // MapleStory party chat does not use “收 + 职业/人” as a recruiting form.
            // Keep that unnatural/ambiguous family out of both party directions instead
            // of teaching the model a made-up convention.  “收 + 道具” remains a buy.
            if (Regex.IsMatch(compact,
                @"^(?:(?:队伍?|本队|我们(?:队|队伍)?)(?:还|再)?)?收" +
                @"(?:一个|一名|[0-9一二三四五六七八九]+个?)?" +
                @"(?:我|人|" + PartyRoleBody + @")(?:吗|么)?$"))
                return OutboundChatIntent.General;

            if (Regex.IsMatch(compact, @"^(?:收|求购|买入|想买|要买|wtb|b>)"))
                return New(OutboundChatIntentKind.Buy);
            if (Regex.IsMatch(compact, @"^(?:卖|出售|想卖|要卖|wts|s>)"))
                return New(OutboundChatIntentKind.Sell);
            if (Regex.IsMatch(compact,
                @"^(?:拿.+换|用.+换|交换|互换|想换(?!频道|线)|换(?!频道|线|ch)|wtt|t>)") ||
                Regex.IsMatch(compact, @"^.+换(?!频道|线|ch).+$"))
                return New(OutboundChatIntentKind.Trade);
            if (Regex.IsMatch(compact,
                @"(?:多少钱|什么价|多少枫币|价格多少|估价|问价|^pc(?:一下)?$)"))
                return New(OutboundChatIntentKind.PriceCheck);

            // Questions such as “有人缺一个远程吗” describe the speaker looking for a
            // team.  They must be recognized before the more general “缺人” recruiter rule.
            if (Regex.IsMatch(compact,
                    @"(?:求组|求队|找队|找组|申请(?:入队|组队)|想(?:进|加|加入)(?:队|队伍)|" +
                    @"能(?:进|加|加入)(?:队|队伍)|j>|lfg|lfp)") ||
                Regex.IsMatch(compact,
                    @"(?:有(?:没有|没)?队(?:伍)?(?:要|缺)|(?:哪个|哪支|谁的)队(?:伍)?缺)" +
                    @"(?:一个|一名|1个|1名)?" + PartyRole + @"(?:吗|么)?$") ||
                Regex.IsMatch(compact,
                    @"(?:有人|有队|哪个队|哪队|谁的队|队里)(?:还)?缺(?:一个|一名|1个|1名)?" +
                    PartyRole + @"(?:吗|么)$") ||
                Regex.IsMatch(compact, PartyRole +
                    @".*有(?:没有|没)?队(?:伍)?(?:要|缺)(?:人|位置)?(?:吗|么)$") ||
                Regex.IsMatch(compact, @"^(?:还)?缺" + PartyRole + @"(?:吗|么)$") ||
                Regex.IsMatch(compact, @"(?:还有|有)(?:队伍)?位置(?:吗|么)$"))
                return New(OutboundChatIntentKind.PartyJoin);

            if (Regex.IsMatch(compact, @"[0-9一二三四五六七八九]+(?:人)?缺[0-9一二三四五六七八九]+") ||
                Regex.IsMatch(compact,
                    @"(?:招|找|组)(?:一个|一名|[0-9一二三四五六七八九]+个?)?" +
                    @"(?:队友|队员|人|" + PartyRoleBody + @")") ||
                Regex.IsMatch(compact,
                    @"(?:队伍?|我们|本队)?(?:还|再)?缺(?:一个|一名|[0-9一二三四五六七八九]+个?)?" +
                    @"(?:人|" + PartyRoleBody + @")(?!吗|么)") ||
                Regex.IsMatch(compact, @"(?:还|再)缺(?:一|1)人(?:有人|来人)(?:吗|么)?$") ||
                Regex.IsMatch(compact, @"(?:有人|谁)(?:要|想)?一起(?:刷|打|做|练级)"))
                return New(OutboundChatIntentKind.PartyRecruit);

            if (Regex.IsMatch(compact,
                @"(?:我(?:可以|能|来)帮|需要我帮|要我帮|我带你|我来带|我教你)"))
                return New(OutboundChatIntentKind.OfferHelp);
            if (Regex.IsMatch(compact,
                @"(?:谁能|谁可以|能不能|可以(?:帮|带)我|帮帮我|帮我|求带|带我|教我|" +
                @"怎么(?:去|做|打|用|回|开|进)|哪里(?:有|能|可以)|在哪(?:里)?(?:接|找|买|打|刷|进))"))
                return New(OutboundChatIntentKind.RequestHelp);

            return OutboundChatIntent.General;
        }

        internal static string BuildPromptConstraint(OutboundChatIntent intent,
            string targetLanguage)
        {
            if (intent == null || intent.Kind == OutboundChatIntentKind.General ||
                !IsOutwardTarget(targetLanguage)) return "";
            switch (intent.Kind)
            {
                case OutboundChatIntentKind.PartyJoin:
                    return "强制语境：说话者本人正在求组、想加入别人的队伍。英语黑话只能按此视角用J>、LFG或LFP；" +
                        "不得写成R>/LFM/LF1，也不得把LF>写成招募某职业。";
                case OutboundChatIntentKind.PartyRecruit:
                    return "强制语境：说话者代表自己的队伍招人。可用R>、LFM、LF1或LF> 1 more；" +
                        "不得写成J>/LFG/LFP求加入。";
                case OutboundChatIntentKind.Buy:
                    return "强制语境：说话者是买家/收购方；只能用B>或WTB方向，不得翻成出售。";
                case OutboundChatIntentKind.Sell:
                    return "强制语境：说话者是卖家/出售方；只能用S>或WTS方向，不得翻成收购。";
                case OutboundChatIntentKind.Trade:
                    return "强制语境：说话者想交换物品；用T>或WTT方向，不得擅自改成买或卖。";
                case OutboundChatIntentKind.PriceCheck:
                    return "强制语境：说话者只是在问价/估价，可用pc；不得擅自改成买入、出售或招募。";
                case OutboundChatIntentKind.RequestHelp:
                    return "强制语境：说话者正在提问或求助；保留请求者视角，不得改成招募、交易或主动提供帮助。";
                case OutboundChatIntentKind.OfferHelp:
                    return "强制语境：说话者正在主动提供帮助；保留帮助者视角，不得改成求助、招募或交易。";
                default:
                    return "";
            }
        }

        internal static string AppendPromptConstraint(string glossary,
            OutboundChatIntent intent, string targetLanguage)
        {
            string constraint = BuildPromptConstraint(intent, targetLanguage);
            if (constraint.Length == 0) return glossary ?? "";
            return constraint + "\n" + (glossary ?? "");
        }

        internal static string NormalizeTranslation(string source, string translated,
            string targetLanguage, OutboundChatIntent intent)
        {
            string result = (translated ?? "").Trim();
            if (result.Length == 0 || intent == null || !IsOutwardTarget(targetLanguage))
                return result;
            if (intent.Kind == OutboundChatIntentKind.PartyJoin)
            {
                // LF> means “looking for ...” and is valid when a party is seeking a
                // member, but it reverses the speaker in a sentence asking to join.
                result = Regex.Replace(result,
                    @"^(?:R\s*>|LFM\b|LF\s*1\b|LF1\b|LF\s*>)\s*",
                    "J> ", RegexOptions.IgnoreCase);
            }
            else if (intent.Kind == OutboundChatIntentKind.PartyRecruit)
            {
                result = Regex.Replace(result, @"^(?:J\s*>|LFG\b|LFP\b)\s*",
                    "R> ", RegexOptions.IgnoreCase);
            }
            else if (intent.Kind == OutboundChatIntentKind.Buy)
            {
                result = Regex.Replace(result, @"^(?:S\s*>|WTS\b|T\s*>|WTT\b)\s*",
                    "B> ", RegexOptions.IgnoreCase);
            }
            else if (intent.Kind == OutboundChatIntentKind.Sell)
            {
                result = Regex.Replace(result, @"^(?:B\s*>|WTB\b|T\s*>|WTT\b)\s*",
                    "S> ", RegexOptions.IgnoreCase);
            }
            else if (intent.Kind == OutboundChatIntentKind.Trade)
            {
                result = Regex.Replace(result, @"^(?:B\s*>|WTB\b|S\s*>|WTS\b)\s*",
                    "T> ", RegexOptions.IgnoreCase);
            }
            return Regex.Replace(result, @"\s{2,}", " ").Trim();
        }

        private static OutboundChatIntent New(OutboundChatIntentKind kind)
        {
            return new OutboundChatIntent(kind);
        }

        private static bool IsOutwardTarget(string targetLanguage)
        {
            string target = targetLanguage ?? "";
            return target.IndexOf("英", StringComparison.OrdinalIgnoreCase) >= 0 ||
                target.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0 ||
                target.IndexOf("西班牙", StringComparison.OrdinalIgnoreCase) >= 0 ||
                target.IndexOf("Espa", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
