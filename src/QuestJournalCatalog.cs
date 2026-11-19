using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MapleOverlay
{
    internal sealed class QuestJournalRecord
    {
        internal string Title,ChineseTitle,TaskId,English,Chinese;
        internal HashSet<string> Anchors;
        // Distinct content words of English. Two stages of the same quest share most
        // anchors, so the only reliable separator is how much of this record's own
        // vocabulary the visible paragraph actually contains.
        internal HashSet<string> Tokens;
    }
    internal sealed partial class TranslationStore
    {
        private readonly Dictionary<string,List<QuestJournalRecord>> journalTitles=new Dictionary<string,List<QuestJournalRecord>>(StringComparer.Ordinal);
        // The opening sentence of a journal body is almost always unique. Indexing it turns
        // the 300-record anchor scan into one dictionary lookup, which is what made long
        // chains like Shumi feel like four seconds.
        private readonly Dictionary<string,List<QuestJournalRecord>> journalFirstSentence=new Dictionary<string,List<QuestJournalRecord>>(StringComparer.Ordinal);
        // Inverted anchor index: a visible 3-gram maps straight to the records containing it.
        // Scoring used to walk every record and every anchor (tens of thousands of string
        // searches per frame), which is what made long chains take seconds.
        private readonly Dictionary<string,List<QuestJournalRecord>> journalAnchorIndex=new Dictionary<string,List<QuestJournalRecord>>(StringComparer.Ordinal);
        private readonly List<QuestJournalRecord> journalRecords=new List<QuestJournalRecord>();
        // Quest chain -> its stage ids, and stage id -> chain. The journal window title
        // names the chain while the body belongs to one stage, so resolving only the
        // chain id used to leave every stage record out of the candidate pool.
        private readonly Dictionary<string,List<string>> questChainStages=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> questChainOf=new Dictionary<string,string>(StringComparer.Ordinal);
        // Minimum level per quest id, from the guidebook. A chain shares wording between
        // stages but not the level gate, so this is a cheap third witness when the body
        // coverage is tied.
        private readonly Dictionary<string,int> questLevels=new Dictionary<string,int>(StringComparer.Ordinal);
        // Manually numbered multi-stage chains (任务链编号.tsv). Each stage lists the words
        // that separate it from its siblings: a different caption noun (coin/roll/sack), a
        // different body phrase, or a different requirement. The screen shows that
        // difference even when OCR mangles the whole title, so the stage is selected by
        // identity instead of by similarity.
        private sealed class StageFingerprint
        {
            internal string TaskId="";
            internal string Caption="";
            internal string[] Words=new string[0];
        }
        private readonly Dictionary<string,List<StageFingerprint>> stageFingerprints=
            new Dictionary<string,List<StageFingerprint>>(StringComparer.Ordinal);
        private void LoadStageFingerprints(string baseDir)
        {
            stageFingerprints.Clear();
            if(String.IsNullOrEmpty(baseDir))return;
            string path=Path.Combine(baseDir,"任务链编号.tsv");
            if(!File.Exists(path))return;
            try {
                foreach(string raw in File.ReadLines(path,Encoding.UTF8)) {
                    if(raw.StartsWith("#",StringComparison.Ordinal))continue;
                    string[] p=raw.Split('\t');if(p.Length<6)continue;
                    string taskId=p[2].Trim();string chain=Normalize(p[3]);string words=p[5].Trim();
                    if(taskId.Length==0||chain.Length==0||words.Length==0)continue;
                    List<StageFingerprint> list;
                    if(!stageFingerprints.TryGetValue(chain,out list)){list=new List<StageFingerprint>();stageFingerprints[chain]=list;}
                    list.Add(new StageFingerprint{TaskId=taskId,Caption=p[4].Trim(),
                        Words=words.Split(new char[]{'|'},StringSplitOptions.RemoveEmptyEntries)});
                }
            } catch(Exception) {
                stageFingerprints.Clear();
            }
        }
        internal string ResolveStageFingerprint(string text,string taskId)
        {
            if(String.IsNullOrWhiteSpace(text)||stageFingerprints.Count==0)return "";
            string normalized=Normalize(text);if(normalized.Length==0)return "";
            HashSet<string> observedTokens=JournalTokenSet(normalized);
            string chain="";
            if(!String.IsNullOrEmpty(taskId))questChainOf.TryGetValue(taskId,out chain);
            string key=Normalize(chain??"");
            List<StageFingerprint> stages;
            if(key.Length>0&&stageFingerprints.TryGetValue(key,out stages))
                return MatchStageInChain(normalized,stages);
            // The id is unresolved, so the visible stage caption names the chain. Only a
            // long caption fragment ("lost roll of cash") is compared across chains: it
            // survives OCR damage that destroys the whole title, and the fingerprints
            // themselves still run strictly inside the chain that fragment names.
            string captionTaskId="";int captionLength=0;bool captionTied=false;
            foreach(KeyValuePair<string,List<StageFingerprint>> bucket in stageFingerprints)
                foreach(StageFingerprint stage in bucket.Value) {
                    string caption=Normalize(stage.Caption);
                    if(caption.Length<8)continue;
                    string[] words=caption.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);
                    int longest=0;
                    for(int start=0;start<words.Length;start++) {
                        StringBuilder phrase=new StringBuilder();
                        for(int end=start;end<words.Length;end++) {
                            if(phrase.Length>0)phrase.Append(' ');
                            phrase.Append(words[end]);
                            if(phrase.Length>=8&&normalized.Contains(phrase.ToString()))
                                longest=Math.Max(longest,phrase.Length);
                        }
                    }
                    if(longest>captionLength){captionTaskId=stage.TaskId;captionLength=longest;captionTied=false;}
                    else if(longest==captionLength&&longest>0&&stage.TaskId!=captionTaskId)captionTied=true;
                }
            if(captionTaskId.Length>0&&!captionTied) {
                string captionChain;
                if(questChainOf.TryGetValue(captionTaskId,out captionChain)) {
                    string captionKey=Normalize(captionChain);
                    List<StageFingerprint> captionStages;
                    if(captionKey.Length>0&&stageFingerprints.TryGetValue(captionKey,out captionStages))
                        return MatchStageInChain(normalized,captionStages);
                }
            }
            return "";
        }
        private static string MatchStageInChain(string normalized,List<StageFingerprint> stages)
        {
            string best="";int bestWeight=0;bool tied=false;
            foreach(StageFingerprint stage in stages) {
                int weight=0;
                foreach(string word in stage.Words) {
                    string hit=Normalize(word);
                    if(hit.Length>0&&normalized.Contains(hit))weight+=hit.Length;
                }
                if(weight>bestWeight){best=stage.TaskId;bestWeight=weight;tied=false;}
                else if(weight==bestWeight&&weight>0&&stage.TaskId!=best)tied=true;
            }
            return bestWeight>0&&!tied?best:"";
        }
        // The fingerprint names the stage; this picks the visible state (in progress /
        // completed) from that stage's own records, and never borrows a sibling's body.
        internal QuestJournalRecord FindJournalByTaskId(string taskId,string observed)
        {
            if(String.IsNullOrEmpty(taskId))return null;
            List<QuestJournalRecord> pool=new List<QuestJournalRecord>();
            foreach(QuestJournalRecord record in journalRecords)if(record.TaskId==taskId)pool.Add(record);
            if(pool.Count==0)return null;
            string reason;
            QuestJournalRecord ranked=RankJournals(pool,observed,false,out reason);
            if(ranked!=null)return ranked;
            HashSet<string> observedTokens=JournalTokenSet(observed);
            QuestJournalRecord best=pool[0];float bestCoverage=-1f;
            foreach(QuestJournalRecord record in pool) {
                float coverage=JournalTokenJaccard(observedTokens,record.Tokens);
                if(coverage>bestCoverage){bestCoverage=coverage;best=record;}
            }
            return best;
        }
        internal void LoadQuestLevels(string baseDir)
        {
            questLevels.Clear();
            if(String.IsNullOrEmpty(baseDir))return;
            string path=Path.Combine(baseDir,"任务等级.json");
            if(!File.Exists(path))return;
            try {
                Dictionary<string,object> root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(
                    File.ReadAllText(path,Encoding.UTF8));
                foreach(KeyValuePair<string,object> pair in root) {
                    int level;
                    if(Int32.TryParse(Convert.ToString(pair.Value),out level))questLevels[pair.Key]=level;
                }
            } catch(Exception) {
                questLevels.Clear();
            }
        }
        // Level gate ("Level 35+") parsed from the same frame. Stages of one chain share
        // wording but not their required level, so it breaks ties the body cannot.
        internal int ScreenLevel;
        internal int QuestLevel(string taskId)
        {
            int level;
            return !String.IsNullOrEmpty(taskId)&&questLevels.TryGetValue(taskId,out level)?level:-1;
        }
        private void LoadQuestChains(string baseDir)
        {
            questChainStages.Clear();questChainOf.Clear();
            if(String.IsNullOrEmpty(baseDir))return;
            string path=Path.Combine(baseDir,"任务链.json");
            if(!File.Exists(path))return;
            try {
                Dictionary<string,object> root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(
                    File.ReadAllText(path,Encoding.UTF8));
                object stagesValue;
                if(root.TryGetValue("stages",out stagesValue)) {
                    Dictionary<string,object> stages=stagesValue as Dictionary<string,object>;
                    if(stages!=null)foreach(KeyValuePair<string,object> pair in stages) {
                        object[] items=pair.Value as object[];
                        if(items==null)continue;
                        List<string> ids=new List<string>();
                        foreach(object item in items)if(item!=null)ids.Add(Convert.ToString(item));
                        if(ids.Count>1)questChainStages[pair.Key]=ids;
                    }
                }
                object chainOfValue;
                if(root.TryGetValue("chainOf",out chainOfValue)) {
                    Dictionary<string,object> chainOf=chainOfValue as Dictionary<string,object>;
                    if(chainOf!=null)foreach(KeyValuePair<string,object> pair in chainOf)
                        questChainOf[Convert.ToString(pair.Key)]=Convert.ToString(pair.Value);
                }
            } catch(Exception) {
                questChainStages.Clear();questChainOf.Clear();
            }
        }
        private void BuildJournalIndex(List<TranslationEntry> entries)
        {
            journalTitles.Clear();journalRecords.Clear();journalFirstSentence.Clear();journalAnchorIndex.Clear();
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
                QuestJournalRecord record=new QuestJournalRecord{Title=StripJournalSourceTag(p[0]),ChineseTitle=p[1],English=p[2],Chinese=p[3].Replace("\\n","\n"),TaskId=p.Length>4?p[4]:""};
                AddJournalTitle(record.Title,record);
                if(p.Length>5)foreach(string alias in p[5].Split('|'))if(alias.Length>0)AddJournalTitle(StripJournalSourceTag(alias),record);
            }
        }
        // Importers once appended a provenance tag to the English title
        // ("Special Taste of Florina Beach I | NiaMeowDB"). The tag is not part of the
        // visible title, but it becomes the dictionary key, so the stage can never be
        // found. Strip a trailing short ASCII "| ...DB" tag at load; localized titles
        // and titles that legitimately contain other bars are left untouched.
        private static string StripJournalSourceTag(string title)
        {
            if(String.IsNullOrWhiteSpace(title))return title;
            int bar=title.LastIndexOf('|');if(bar<0)return title;
            string tag=title.Substring(bar+1).Trim();
            if(tag.Length==0||tag.Length>24)return title;
            if(!Char.IsLetter(tag[0])||!tag.EndsWith("DB",StringComparison.OrdinalIgnoreCase))return title;
            foreach(char c in tag)if(c>127)return title;
            return title.Substring(0,bar).TrimEnd();
        }
        private void AddJournalTitle(string title,QuestJournalRecord record)
        {
            if(String.IsNullOrWhiteSpace(record.English)||String.IsNullOrWhiteSpace(record.Chinese))return;
            string key=Normalize(title);List<QuestJournalRecord> list;
            if(!journalTitles.TryGetValue(key,out list)){list=new List<QuestJournalRecord>();journalTitles.Add(key,list);}
            foreach(QuestJournalRecord previous in list)if(previous.TaskId==record.TaskId&&previous.English==record.English&&previous.Chinese==record.Chinese)return;
            record.Anchors=JournalAnchors(record.English);
            record.Tokens=JournalTokenSet(record.English);
            list.Add(record);
            foreach(string anchor in record.Anchors) {
                List<QuestJournalRecord> anchorBucket;
                if(!journalAnchorIndex.TryGetValue(anchor,out anchorBucket)){anchorBucket=new List<QuestJournalRecord>();journalAnchorIndex[anchor]=anchorBucket;}
                if(!anchorBucket.Contains(record))anchorBucket.Add(record);
            }
            bool known=false;foreach(QuestJournalRecord old in journalRecords)if(old.TaskId==record.TaskId&&old.Title==record.Title&&old.English==record.English&&old.Chinese==record.Chinese){known=true;break;}
            if(!known){journalRecords.Add(record);IndexJournalFirstSentence(record);}
        }
        private static HashSet<string> JournalAnchors(string source)
        {
            string[] words=Normalize(source).Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);HashSet<string> result=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i+2<words.Length;i++)result.Add(words[i]+" "+words[i+1]+" "+words[i+2]);return result;
        }
        // Content words only: "the/of/and" appear in every stage and would flatten the
        // similarity between siblings.
        private static HashSet<string> JournalTokenSet(string source)
        {
            HashSet<string> result=new HashSet<string>(StringComparer.Ordinal);
            foreach(string word in Normalize(source??"").Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries))
                if(word.Length>2)result.Add(word);
            return result;
        }
        internal bool HasJournalTitle(string title)
        {return !String.IsNullOrWhiteSpace(title)&&!title.Contains("...")&&!title.Contains("…")&&journalTitles.ContainsKey(Normalize(title));}
        private readonly Dictionary<string,string> journalStageCache=new Dictionary<string,string>(StringComparer.Ordinal);
        internal string ResolveJournalStage(string text)
        {
            string stageKey=Normalize(text??"");
            string stageCached;
            if(stageKey.Length>0&&journalStageCache.TryGetValue(stageKey,out stageCached))return stageCached;
            string stageResult=ResolveJournalStageCore(text);
            if(stageKey.Length>0){if(journalStageCache.Count>512)journalStageCache.Clear();journalStageCache[stageKey]=stageResult;}
            return stageResult;
        }
        private string ResolveJournalStageCore(string text)
        {
            if(HasJournalTitle(text))return text;
            if(String.IsNullOrWhiteSpace(text)||text.Contains("...")||text.Contains("…"))return "";
            string key=Regex.Replace(Normalize(text),@"[^\p{L}\p{N}]","");if(key.Length<10||key.Length>100)return "";
            string best="";float score=0,second=0;
            foreach(string title in journalTitles.Keys) {
                string compact=Regex.Replace(title,@"[^\p{L}\p{N}]","");if(Math.Abs(compact.Length-key.Length)>3)continue;
                float value=compact==key?1:TextSimilarity(key,compact);if(value<.87f)continue;
                // An exact compact match (OCR only inserted spaces/punctuation) is
                // unambiguous even when sibling stages ("50/70/100 Cursed Dolls") score
                // 0.95 too; the old gap rule rejected the whole family.
                if(compact==key)return title;
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
        // Every caption row on screen (the selected quest title, the stage title, and the
        // task-list names) narrows the journal pool before prose similarity is scored.
        internal static string ExtractChineseCore(string value)
        {
            StringBuilder text=new StringBuilder();
            foreach(char c in value??"")
                if((c>='一'&&c<='鿿')||c=='/'||c=='／')text.Append(c);
            return text.ToString();
        }
        internal List<QuestJournalRecord> JournalRecordsForCaption(string caption)
        {
            string trimmed=(caption??"").Trim();
            string key=ResolveJournalStage(trimmed);
            if(key.Length>0) {
                List<QuestJournalRecord> records;
                if(journalTitles.TryGetValue(Normalize(key),out records))return records;
            }
            // A frame after the first translate already shows the painted Chinese caption.
            // Matching it against the stored stage titles keeps the pool narrow there too.
            List<QuestJournalRecord> byChinese=new List<QuestJournalRecord>();
            foreach(QuestJournalRecord record in journalRecords) {
                if(record.ChineseTitle.Length==0)continue;
                if(trimmed.Contains(record.ChineseTitle)||record.ChineseTitle.Contains(trimmed))
                    byChinese.Add(record);
            }
            if(byChinese.Count>0)return byChinese;
            // Mixed rows ("Make a 三眼章鱼/蘑菇五子棋") keep the English prefix next to the
            // painted Chinese. Compare just the Chinese core so the pool still narrows.
            // A clipped caption ("Maple World's Unique F...") still identifies the chain:
            // accept a long-enough prefix of any indexed title or alias.
            string prefix=trimmed.TrimEnd('.','…');
            if(prefix.Length>=12) {
                List<QuestJournalRecord> byPrefix=new List<QuestJournalRecord>();
                foreach(KeyValuePair<string,List<QuestJournalRecord>> entry in journalTitles) {
                    if(!entry.Key.StartsWith(Normalize(prefix),StringComparison.Ordinal))continue;
                    foreach(QuestJournalRecord record in entry.Value)
                        if(!byPrefix.Contains(record))byPrefix.Add(record);
                }
                if(byPrefix.Count>0)return byPrefix;
            }
            string chineseCore=ExtractChineseCore(trimmed);
            if(chineseCore.Length<4)return null;
            foreach(QuestJournalRecord record in journalRecords) {
                if(record.ChineseTitle.Length==0)continue;
                if(record.ChineseTitle.Contains(chineseCore)||chineseCore.Contains(record.ChineseTitle))
                    byChinese.Add(record);
            }
            return byChinese.Count>0?byChinese:null;
        }
        internal QuestJournalRecord SelectJournalFromRecords(List<QuestJournalRecord> records,
            string observed,out string reason)
        {return records==null?RankJournals(journalRecords,observed,false,out reason):
            RankJournals(records,observed,true,out reason);}
        internal QuestJournalRecord SelectJournalForProse(string observed,out string reason)
        {return SelectJournalForProse(observed,"",out reason);}
        // The visible caption already names one quest. Restricting the prose pool to that
        // quest first is what stops the near-identical Omok bodies from swapping Chinese.
        private static string JournalFirstSentence(string english)
        {
            if(String.IsNullOrEmpty(english))return "";
            Match match=Regex.Match(english,@"^.{8,120}?[.!?](?:\\s|$)");
            if(!match.Success)return "";
            return Normalize(match.Value.Trim());
        }
        private void IndexJournalFirstSentence(QuestJournalRecord record)
        {
            string first=JournalFirstSentence(record.English);
            if(first.Length==0)return;
            List<QuestJournalRecord> bucket;
            if(!journalFirstSentence.TryGetValue(first,out bucket)){bucket=new List<QuestJournalRecord>();journalFirstSentence[first]=bucket;}
            bucket.Add(record);
        }
        internal QuestJournalRecord SelectJournalForProse(string observed,string preferredTaskId,out string reason)
        {
            string observedFirst=JournalFirstSentence(observed);
            if(observedFirst.Length>0) {
                List<QuestJournalRecord> bucket;
                if(journalFirstSentence.TryGetValue(observedFirst,out bucket)) {
                    List<QuestJournalRecord> candidates=bucket;
                    if(!String.IsNullOrEmpty(preferredTaskId)) {
                        List<QuestJournalRecord> filtered=new List<QuestJournalRecord>();
                        foreach(QuestJournalRecord record in bucket)if(record.TaskId==preferredTaskId)filtered.Add(record);
                        if(filtered.Count>0)candidates=filtered;
                    }
                    if(candidates.Count==1){reason="first-sentence";return candidates[0];}
                    QuestJournalRecord ranked=RankJournals(candidates,observed,true,out reason);
                    if(ranked!=null){reason="first-sentence-ranked";return ranked;}
                }
            }
            List<QuestJournalRecord> pool=journalRecords;
            if(!String.IsNullOrEmpty(preferredTaskId)) {
                // The resolved id names one stage of a chain ("A Clue to the Secret Book",
                // 10500) while the visible body belongs to a sibling stage ("Thirsty
                // Ronnie", 10505). Pool the whole chain, not the single id.
                HashSet<string> wanted=new HashSet<string>(StringComparer.Ordinal);
                wanted.Add(preferredTaskId);
                string chainName;
                if(questChainOf.TryGetValue(preferredTaskId,out chainName)) {
                    List<string> chainStages;
                    if(questChainStages.TryGetValue(chainName,out chainStages))
                        foreach(string stageId in chainStages)wanted.Add(stageId);
                }
                List<QuestJournalRecord> filtered=new List<QuestJournalRecord>();
                foreach(QuestJournalRecord record in journalRecords)
                    if(wanted.Contains(record.TaskId))filtered.Add(record);
                if(filtered.Count>0)pool=filtered;
            }
            // The window title names the quest chain ("Mr. Wetbottom's Secret Book"),
            // the body belongs to a stage of it ("Thirsty Ronnie", id 10505). Restricting
            // the pool to the chain id finds nothing and the whole panel stays English.
            // Only an id-free prose match may rescue that case; the coverage gate inside
            // RankJournals still rejects siblings and unrelated quests.
            if(pool!=journalRecords) {
                QuestJournalRecord byId=RankJournals(pool,observed,false,out reason);
                if(byId!=null)return byId;
            }
            return RankJournals(journalRecords,observed,false,out reason);
        }
        private QuestJournalRecord RankJournals(List<QuestJournalRecord> records,string observed,bool titleKnown,out string reason)
        {
            reason="ambiguous-journal-state";string text=Normalize(observed);QuestJournalRecord best=null;int bestScore=-1,second=-1;
            // Coverage is the fraction of a record's own anchors that the visible body
            // actually contains. A quest with several stages shares most anchors between
            // them, so the raw anchor count ties; coverage separates the stage that the
            // player is looking at from the sibling stages of the same quest.
            float bestCoverage=-1f,secondCoverage=-1f;
            HashSet<string> translations=new HashSet<string>(StringComparer.Ordinal);foreach(QuestJournalRecord record in records)translations.Add(record.Chinese);
            // Score through the inverted anchor index: only the 3-grams actually visible in
            // this text are looked up, instead of scanning every record's whole anchor set.
            HashSet<QuestJournalRecord> pool=new HashSet<QuestJournalRecord>(records);
            Dictionary<QuestJournalRecord,int> scores=new Dictionary<QuestJournalRecord,int>();
            string[] words=text.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);
            for(int i=0;i+2<words.Length;i++) {
                List<QuestJournalRecord> bucket;
                if(!journalAnchorIndex.TryGetValue(words[i]+" "+words[i+1]+" "+words[i+2],out bucket))continue;
                foreach(QuestJournalRecord record in bucket) {
                    if(!pool.Contains(record))continue;
                    int score;scores.TryGetValue(record,out score);scores[record]=score+1;
                }
            }
            HashSet<string> observedTokens=JournalTokenSet(observed);
            foreach(QuestJournalRecord record in records) {
                int score;scores.TryGetValue(record,out score);
                if(!JournalNumbersCompatible(observed,record.English))continue;
                // Jaccard over content words: sibling stages share anchors but differ in
                // their own nouns and verbs, which is exactly what separates them.
                float coverage=JournalTokenJaccard(observedTokens,record.Tokens);
                // A chain's stages sit within a few levels of each other, so the visible
                // level gate is a real tie-breaker: the wrong stage is usually 10+ levels
                // away, while the right one is within 3.
                int stageLevel=QuestLevel(record.TaskId);
                if(stageLevel>=0&&ScreenLevel>0) {
                    int levelGap=Math.Abs(stageLevel-ScreenLevel);
                    if(levelGap<=3)coverage+=0.08f;
                    else if(levelGap>=15)coverage-=0.08f;
                }
                if(best!=null&&best.Chinese==record.Chinese){
                    if(coverage>bestCoverage||(coverage==bestCoverage&&score>bestScore)){
                        best=record;bestCoverage=coverage;bestScore=score;
                    }
                    continue;
                }
                bool stronger=coverage>bestCoverage||
                    (Math.Abs(coverage-bestCoverage)<0.0005f&&score>bestScore);
                if(stronger){
                    secondCoverage=bestCoverage;second=bestScore;
                    bestCoverage=coverage;bestScore=score;best=record;
                } else {
                    secondCoverage=Math.Max(secondCoverage,coverage);
                    second=Math.Max(second,score);
                }
            }
            if(best==null){reason="quantity-version-mismatch";return null;}
            if(titleKnown&&translations.Count==1){reason="stage-title";return best;}
            // The visible body should cover most of the chosen record; a stage that only
            // matches a couple of shared anchors is a sibling, not this screen. Loosening
            // the gap to 0.03 let a sibling win on frames that previously stayed undecided,
            // which turned readable quests (Florina Beach) into fully English panes. Keep
            // the original guard: undecided is recoverable, a wrong stage is not.
            if(bestCoverage>=0.30f&&bestCoverage-secondCoverage>=0.06f)
            {reason="prose-coverage";return best;}
            if(bestScore>=(titleKnown?2:3)&&(titleKnown?bestScore>second:bestScore-second>=2))
            {reason=titleKnown?"stage-and-state-anchor":"unique-prose-anchors";return best;}
            return null;
        }

        private static float JournalTokenJaccard(HashSet<string> observed,HashSet<string> record)
        {
            if(observed==null||record==null||record.Count==0)return 0f;
            int hit=0;
            foreach(string token in record)if(observed.Contains(token))hit++;
            int union=observed.Count+record.Count-hit;
            return union<=0?0f:(float)hit/union;
        }
        internal bool JournalMemoryMatches(QuestJournalRecord record,string observed)
        {
            if(!JournalNumbersCompatible(observed,record.English))return false;int count=0;string text=Normalize(observed);
            foreach(string anchor in record.Anchors)if(text.Contains(anchor)&&++count>=2)return true;return false;
        }
        private static bool JournalNumbersCompatible(string observed,string english)
        {
            // The journal paints live counters next to the body ("33 / 30", "0/25"), and
            // Windows OCR reads the separator as /, ! or l. Those are UI progress values,
            // not prose quantities: keeping them made every body fail with
            // "quantity-version-mismatch" while the real quantity ("30") was correct.
            string prose=Regex.Replace(observed??"",@"\d+\s*[/!|lI]\s*\d+"," ");
            // The level gate ("Level 32+") is chrome, not prose: its number never
            // appears in the task body and used to reject every candidate record.
            prose=Regex.Replace(prose,@"\blevel\s*\d+\s*\+?"," ",RegexOptions.IgnoreCase);
            HashSet<string> expected=new HashSet<string>(StringComparer.Ordinal);
            foreach(Match m in Regex.Matches(english??"",@"[+\-]?\d+(?:\.\d+)?%?"))expected.Add(m.Value);
            foreach(Match m in Regex.Matches(prose,@"[+\-]?\d+(?:\.\d+)?%?")) {
                // A lone digit next to the summary card ("0 1" for 0/1) is a UI
                // counter whose slash OCR dropped; it is never a prose quantity.
                if(m.Value.TrimStart('+','-').Length<=1)continue;
                if(expected.Contains(m.Value))continue;
                // One misread digit ("30" -> "80") is OCR, not a different quest
                // version: the same glyph shapes differ by a single stroke. Anything
                // further apart stays incompatible and rejects the record.
                bool close=false;
                foreach(string candidate in expected)
                    if(JournalNumbersClose(candidate,m.Value)){close=true;break;}
                if(!close)return false;
            }
            return true;
        }

        private static bool JournalNumbersClose(string expected,string observed)
        {
            if(expected.Length!=observed.Length||expected.Length==0||expected.Length>4)return false;
            int diff=0;
            for(int i=0;i<expected.Length;i++)
                if(expected[i]!=observed[i]&&++diff>1)return false;
            return diff==1;
        }
    }
}
