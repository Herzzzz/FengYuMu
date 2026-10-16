using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class QuestJournalRecord
    {
        internal string Title,ChineseTitle,TaskId,English,Chinese;
        internal HashSet<string> Anchors;
    }
    internal sealed partial class TranslationStore
    {
        private readonly Dictionary<string,List<QuestJournalRecord>> journalTitles=new Dictionary<string,List<QuestJournalRecord>>(StringComparer.Ordinal);
        private readonly List<QuestJournalRecord> journalRecords=new List<QuestJournalRecord>();
        private void BuildJournalIndex(List<TranslationEntry> entries)
        {
            journalTitles.Clear();journalRecords.Clear();
            foreach(TranslationEntry title in taskNames) {
                if(title.TaskId.Length==0)continue;
                foreach(TranslationEntry body in taskEntries) {
                    if(body.TaskId!=title.TaskId || !body.Category.StartsWith("怀旧服-任务说明#",StringComparison.Ordinal))continue;
                    AddJournalTitle(title.English,new QuestJournalRecord{Title=title.English,ChineseTitle=title.Chinese,TaskId=title.TaskId,English=body.English,Chinese=body.Chinese});
                }
            }
            // Shared chain prose has no canonical quest ID. It can be selected
            // by unique visible prose, but never guessed from an unrelated title.
            foreach(TranslationEntry body in entries)
                if(body.TaskId.Length==0 && body.Category=="怀旧服-任务说明" && body.English.Length>=50) {
                    bool known=false;foreach(QuestJournalRecord old in journalRecords)
                        if(old.English==body.English&&old.Chinese==body.Chinese){known=true;break;}
                    if(!known)journalRecords.Add(new QuestJournalRecord{Title="",ChineseTitle="",TaskId="",
                        English=body.English,Chinese=body.Chinese,Anchors=JournalAnchors(body.English)});
                }
            // Server variants and aliases are ordinary data records, never branches
            // in recognition code. An absent optional file does not change user TSVs.
            string path=Path.Combine(Path.GetDirectoryName(sourcePath),"任务正文.tsv");
            if(File.Exists(path))foreach(string raw in File.ReadLines(path,Encoding.UTF8)) {
                if(raw.StartsWith("#",StringComparison.Ordinal))continue;string[] p=raw.Split('\t');if(p.Length<4)continue;
                QuestJournalRecord record=new QuestJournalRecord{Title=p[0],ChineseTitle=p[1],English=p[2],Chinese=p[3].Replace("\\n","\n"),TaskId=p.Length>4?p[4]:""};
                AddJournalTitle(record.Title,record);
                if(p.Length>5)foreach(string alias in p[5].Split('|'))if(alias.Length>0)AddJournalTitle(alias,record);
            }
        }
        private void AddJournalTitle(string title,QuestJournalRecord record)
        {
            if(String.IsNullOrWhiteSpace(record.English)||String.IsNullOrWhiteSpace(record.Chinese))return;
            string key=Normalize(title);List<QuestJournalRecord> list;
            if(!journalTitles.TryGetValue(key,out list)){list=new List<QuestJournalRecord>();journalTitles.Add(key,list);}
            foreach(QuestJournalRecord previous in list)if(previous.TaskId==record.TaskId&&previous.English==record.English&&previous.Chinese==record.Chinese)return;
            record.Anchors=JournalAnchors(record.English);list.Add(record);
            bool known=false;foreach(QuestJournalRecord old in journalRecords)if(old.TaskId==record.TaskId&&old.Title==record.Title&&old.English==record.English&&old.Chinese==record.Chinese){known=true;break;}
            if(!known)journalRecords.Add(record);
        }
        private static HashSet<string> JournalAnchors(string source)
        {
            string[] words=Normalize(source).Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);HashSet<string> result=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i+2<words.Length;i++)result.Add(words[i]+" "+words[i+1]+" "+words[i+2]);return result;
        }
        internal bool HasJournalTitle(string title)
        {return !String.IsNullOrWhiteSpace(title)&&!title.Contains("...")&&!title.Contains("…")&&journalTitles.ContainsKey(Normalize(title));}
        internal string ResolveJournalStage(string text)
        {
            if(HasJournalTitle(text))return text;
            if(String.IsNullOrWhiteSpace(text)||text.Contains("...")||text.Contains("…"))return "";
            string key=Regex.Replace(Normalize(text),@"[^\p{L}\p{N}]","");if(key.Length<10||key.Length>100)return "";
            string best="";float score=0,second=0;
            foreach(string title in journalTitles.Keys) {
                string compact=Regex.Replace(title,@"[^\p{L}\p{N}]","");if(Math.Abs(compact.Length-key.Length)>3)continue;
                float value=compact==key?1:TextSimilarity(key,compact);if(value<.91f)continue;
                if(value>score){second=score;score=value;best=title;}else if(title!=best)second=Math.Max(second,value);
            }
            return score-second>=.06f?best:"";
        }
        internal string ResolveJournalCaption(string text)
        {
            string exact=ResolveJournalStage(text);if(exact.Length>0)return exact;
            if(String.IsNullOrWhiteSpace(text)||(!text.Contains("...")&&!text.Contains("…")))return "";
            string prefix=Normalize(text.Replace("...","").Replace("…","")).Trim();if(prefix.Length<16)return "";
            string found="";HashSet<string> ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(KeyValuePair<string,List<QuestJournalRecord>> pair in journalTitles)if(pair.Key.StartsWith(prefix,StringComparison.Ordinal)) {
                foreach(QuestJournalRecord record in pair.Value)ids.Add(JournalIdentity(record));found=pair.Key;
            }
            return ids.Count==1?found:"";
        }
        private static string JournalIdentity(QuestJournalRecord record)
        {return record.TaskId.Length>0?record.TaskId:Normalize(record.Title);}
        internal string JournalTitleForTranslation(string chinese)
        {
            string title="",id="";
            foreach(QuestJournalRecord record in journalRecords)if(record.Chinese==chinese) {
                string candidate=JournalIdentity(record);if(id.Length>0&&id!=candidate)return "";title=record.Title;id=candidate;
            }
            return title;
        }
        internal QuestJournalRecord SelectJournal(string title,string observed,out string reason)
        {
            string key=ResolveJournalStage(title);List<QuestJournalRecord> records;reason="unknown-stage";
            if(key.Length==0||!journalTitles.TryGetValue(Normalize(key),out records))return null;
            HashSet<string> ids=new HashSet<string>(StringComparer.Ordinal);foreach(QuestJournalRecord r in records)ids.Add(JournalIdentity(r));
            if(ids.Count!=1){reason="ambiguous-stage";return null;}
            return RankJournals(records,observed,true,out reason);
        }
        internal QuestJournalRecord SelectJournalForProse(string observed,out string reason)
        {return RankJournals(journalRecords,observed,false,out reason);}
        private static QuestJournalRecord RankJournals(List<QuestJournalRecord> records,string observed,bool titleKnown,out string reason)
        {
            reason="ambiguous-journal-state";string text=Normalize(observed);QuestJournalRecord best=null;int bestScore=-1,second=-1;
            HashSet<string> translations=new HashSet<string>(StringComparer.Ordinal);foreach(QuestJournalRecord record in records)translations.Add(record.Chinese);
            foreach(QuestJournalRecord record in records) {
                if(!JournalNumbersCompatible(observed,record.English))continue;
                int score=0;foreach(string phrase in record.Anchors)if(text.Contains(phrase))score++;
                if(best!=null&&best.Chinese==record.Chinese){if(score>bestScore){best=record;bestScore=score;}continue;}
                if(score>bestScore){second=bestScore;bestScore=score;best=record;}else second=Math.Max(second,score);
            }
            if(best==null){reason="quantity-version-mismatch";return null;}
            if(titleKnown&&translations.Count==1){reason="stage-title";return best;}
            if(bestScore>=(titleKnown?2:3)&&bestScore-second>=2){reason=titleKnown?"stage-and-state-anchor":"unique-prose-anchors";return best;}
            return null;
        }
        internal bool JournalMemoryMatches(QuestJournalRecord record,string observed)
        {
            if(!JournalNumbersCompatible(observed,record.English))return false;int count=0;string text=Normalize(observed);
            foreach(string anchor in record.Anchors)if(text.Contains(anchor)&&++count>=2)return true;return false;
        }
        private static bool JournalNumbersCompatible(string observed,string english)
        {
            HashSet<string> expected=new HashSet<string>(StringComparer.Ordinal);
            foreach(Match m in Regex.Matches(english??"",@"[+\-]?\d+(?:\.\d+)?%?"))expected.Add(m.Value);
            foreach(Match m in Regex.Matches(observed??"",@"[+\-]?\d+(?:\.\d+)?%?"))if(!expected.Contains(m.Value))return false;
            return true;
        }
    }
}
