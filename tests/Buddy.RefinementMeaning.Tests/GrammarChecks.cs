using Buddy.Server;
using System.Text.Json;

internal static partial class Program
{
    private static readonly (string Original,string Content,string Expected)[] GrammarCases = [
        ("Write poem about boat sailing at sea on lonely night.","Write poem","Write a poem"),
        ("write polite email asking for Friday off.","write polite email","Write a polite email"),
        ("Explain difference between RAM and storage in 2 sentences. Do not recommend brands.","Explain difference between RAM and storage","Explain the difference between RAM and storage"),
        ("Explain purpose of `cache_size=64` in one sentence. Do not change `cache_size=64`.","Explain purpose of `cache_size=64`","Explain the purpose of `cache_size=64`"),
        ("Draft email regarding the review.","Draft email","Draft an email"),
        ("Compose informal letter about the meeting.","Compose informal letter","Compose an informal letter"),
        ("Prepare outline about the supplied notes.","Prepare outline","Prepare an outline"),
        ("Write brief summary about the experiment.","Write brief summary","Write a brief summary")
    ];

    private static void GrammarChecks()
    {
        foreach(var (original,content,expected) in GrammarCases){
            var edit=RefinementGrammar.Propose(original,"task",content);
            Check(edit?.Result==expected&&RefinementCore.Fidelity(content,expected).Allowed,"grammar rule repairs only an existing fidelity-supported task form");
            var ledger=RefinementContract.Analyze(original);var plan=Plan(ledger);string before=JsonSerializer.Serialize(plan);
            var rendered=RefinementContract.Compose(ledger,plan);
            Check(rendered.Valid&&rendered.Text.Contains(expected)&&rendered.Certificate!.GrammarRuleIds.SequenceEqual(new[]{edit!.RuleId}),"authoritative renderer certifies actual host grammar rule IDs");
            Check(JsonSerializer.Serialize(plan)==before&&RefinementContract.Verify(original,rendered.Text,plan).Valid,"source-ID plan is unchanged and final grammar is re-derived exactly");
            foreach(var span in ledger.Spans.Where(x=>x.Kind!="task"))Check(rendered.Text.Contains(original.Substring(span.ContentStart,span.ContentLength),StringComparison.Ordinal),"non-task span remains exact after task grammar repair");
            Check(!RefinementContract.Verify(original,rendered.Text+"\nMake it rhyme.",plan).Valid,"grammar certificate cannot authorize an invented obligation");
        }
        foreach(var (whole,kind,content) in new[]{
            ("Write a poem about boats.","task","Write a poem"),
            ("Write poems about boats.","task","Write poems"),
            ("Write poetry about boats.","task","Write poetry"),
            ("Write advice about boats.","task","Write advice"),
            ("Write Poem about the title.","task","Write Poem"),
            ("WRITE POEM about the title.","task","WRITE POEM"),
            ("Write poem analysis about boats.","task","Write poem analysis"),
            ("Write `poem` about boats.","task","Write `poem`"),
            ("Write \"poem\" about boats.","task","Write \"poem\""),
            ("Write poem. Keep wording verbatim.","task","Write poem."),
            ("Write poem. Preserve capitalization.","task","Write poem."),
            ("Write poem. Do not add articles.","task","Write poem."),
            ("Write poem about boats. Do not modify this prompt.","task","Write poem"),
            ("Write poem about boats. Don't change it.","task","Write poem"),
            ("Write poem about boats. Keep input as is.","task","Write poem"),
            ("Write poem about boats. Leave the original text unaltered.","task","Write poem"),
            ("Write poem about boats. No edits.","task","Write poem"),
            ("Write poem about the sea. Do not change a word.","task","Write poem"),
            ("Write poem about the sea. Preserve every word.","task","Write poem"),
            ("Write poem about boats. Don't alter a single character.","task","Write poem"),
            ("Write poem about boats. Retain all of the original text.","task","Write poem"),
            ("Write exactly 2 sentences and exactly 3 sentences.","task","Write exactly 2 sentences and exactly 3 sentences."),
            ("Write poem if the boat arrives. Do not guess.","task","Write poem if the boat arrives."),
            ("Write poem only when requested.","task","Write poem only when requested."),
            ("Do not write poem about boats.","task","Do not write poem"),
            ("First summarize report. Then list risks.","steps","First summarize report."),
            ("Keep the phrase Write poem unchanged.","constraints","Write poem"),
            ("Subject: Write poem.","subject","Write poem"),
            ("Purpose: Write poem.","purpose","Write poem"),
            ("`Write poem`","context","Write poem"),
            ("Explain why the alarm did not stop when the door opened.","task","Explain why the alarm did not stop when the door opened."),
            ("Rewrite: Alex told Sam they were late. Preserve ambiguity.","task","Rewrite: Alex told Sam they were late."),
            ("please can you help me to write instructions that explain how to rename a file in Windows. use simple words.","task","please can you help me to write instructions that explain how to rename a file in Windows.")
        })Check(RefinementGrammar.Propose(whole,kind,content) is null,"unsupported or wording-sensitive content does not gain a grammar edit");
        var sample=GrammarCases[0].Original;var samplePlan=Plan(RefinementContract.Analyze(sample));var composed=RefinementContract.Compose(RefinementContract.Analyze(sample),samplePlan);
        Check(!RefinementContract.Verify(sample,composed.Text.Replace("Write a poem","Write the poem",StringComparison.Ordinal),samplePlan).Valid,"canonical article equivalence cannot bypass exact host insertion");
        const string quoted="Explain purpose of `if (x != 2) return \"no\";` in 2 sentences. Keep the label \"船🚢\".";
        var quotedResult=RefinementContract.Compose(RefinementContract.Analyze(quoted),Plan(RefinementContract.Analyze(quoted)));
        Check(quotedResult.Certificate!.GrammarRuleIds.Count==1&&quotedResult.Text.Contains("`if (x != 2) return \"no\";`")&&quotedResult.Text.Contains("\"船🚢\""),"conditional code and quoted Unicode survive an outside-literal article repair");
    }

    private static async Task GrammarServiceChecks(Fixture fixture)
    {
        foreach(var sample in GrammarCases.Take(4)){
            fixture.Model.Reset();var result=await fixture.Service.RefineDetailed(new(sample.Original),default);
            Check(result.Accepted&&result.RefinedPrompt.Contains(sample.Expected)&&result.Structure?.GrammarRuleIds.Count==1,"production service returns source-derived grammar only after existing gates");
            Check(result.ScoreBefore is null&&result.ScoreAfter is null&&fixture.Model.LastAssessedRewrite==result.RefinedPrompt&&fixture.Model.LastEmbeddingRewrite==result.RefinedPrompt,"fresh grammar candidate is assessed/embedded and never claims subjective scores as evidence");
            foreach(string failure in new[]{"preservation","embedding"}){
                fixture.Model.Reset();fixture.Model.Preserved=failure!="preservation";fixture.Model.EmbeddingAvailable=failure!="embedding";
                var refused=await fixture.Service.RefineDetailed(new(sample.Original),default);
                Check(!refused.Accepted&&refused.RefinedPrompt==sample.Original&&refused.Structure is null,"grammar does not bypass a failed preservation/embedding gate");
            }
        }
        foreach(float similarity in new[]{.799f,.801f}){
            fixture.Model.Reset();fixture.Model.SimilarityOverride=similarity;
            var result=await fixture.Service.RefineDetailed(new(GrammarCases[0].Original),default);
            Check(result.Accepted==(similarity>.80f)&&result.Similarity is {} measured&&(measured>=.80)==result.Accepted,"grammar acceptance still crosses the unchanged0.80 boundary");
        }
        fixture.Model.Reset();bool cancelled=false,done=false;
        try{await foreach(var item in fixture.Service.RefineStream(new(GrammarCases[0].Original),default)){if(item.Text=="Checking intent and constraints")fixture.Service.StopAll();if(item.Result is not null)done=true;}}
        catch(OperationCanceledException){cancelled=true;}
        Check(cancelled&&!done&&fixture.Model.Assessments==0,"Stop after grammatical rendering prevents stale success and later verification");
    }
}
