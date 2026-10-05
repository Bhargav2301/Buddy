#if HAS_GRAMMAR54
using Buddy.Server;

internal static class GrammarCases {
    internal static void Run(Checks c) {
        foreach(var (source,wanted) in new[]{("Write poem","Write a poem"),("write polite email","Write a polite email"),("Prepare informal letter","Prepare an informal letter"),("Draft outline","Draft an outline"),("Explain difference between salt and sugar","Explain the difference between salt and sugar"),("Explain purpose of cache","Explain the purpose of cache")}) {
            var edit=RefinementGrammar.Propose(source,"task",source);
            c.Check(edit?.Source==source&&edit.Result==wanted,"GRAMMAR-POSITIVE-"+source,"Finite missing-article repair is derived from the exact source.");
        }
        foreach(string unchanged in new[]{"Write a poem","Write poems","Write poem.txt","Write poem about the sea","Write an email","Write urgent report","Prepare SQL","write e-mail","WRITE POEM","Write 2 poems"})
            c.Check(RefinementGrammar.Propose(unchanged,"task",unchanged) is null,"GRAMMAR-NARROW-"+unchanged,"Unsupported task heads are not creatively rewritten.");
        foreach(string qualifier in new[]{"Do not change the prompt.","Do not change a word.","Preserve every word.","Keep the wording as is.","Preserve exactly these words.","Only repair line breaks.","If the sea is calm, keep it brief.","Never alter anything.","No edits."})
            c.Check(RefinementGrammar.Propose("Write poem. "+qualifier,"task","Write poem") is null,"GRAMMAR-OPT-OUT-"+qualifier,"Whole-request qualifications veto article editing.");
        c.Check(RefinementGrammar.Propose("Write poem","subject","Write poem") is null,"GRAMMAR-ROLE","Task-head grammar cannot edit subject or quoted content roles.");
        const string poem="Write poem about boat sailing at sea on lonely night.";
        var ledger=RefinementContract.Analyze(poem);
        var plan=new RefinementContractPlan(ledger.Spans.Select(s=>new RefinementContractSection(s.Kind,[s.Id])).ToList());
        var review=RefinementContract.Compose(ledger,plan);
        c.Check(review.Valid&&review.Useful&&review.Text.Contains("Write a poem.",StringComparison.Ordinal)&&review.Text.Contains("boat sailing at sea on lonely night",StringComparison.Ordinal),"GRAMMAR-SCENE","Article insertion retains the entire boat, sea and night relation.");
        c.Check(review.Certificate?.GrammarRuleIds.Contains("singular-deliverable-article-v1")==true,"GRAMMAR-CERTIFICATE","Certificate identifies the actual deterministic rule rather than a model quality score.");
        c.Check(RefinementContract.Verify(poem,review.Text,plan).Valid&&!RefinementContract.Verify(poem,review.Text.Replace("a poem","the poem",StringComparison.Ordinal),plan).Valid,"GRAMMAR-EXACT-RENDER","Alternative articles cannot pass through generic fidelity's article tolerance.");
        c.Check(!RefinementContract.Verify(poem,review.Text.Replace("lonely night","sunny morning",StringComparison.Ordinal),plan).Valid,"GRAMMAR-RELATION-TAMPER","Recomputation rejects altered scene facts.");
        const string protectedText="Write poem about `x != 7` and \"Do not publish\" on 2026-10-05 😀.";
        var protectedLedger=RefinementContract.Analyze(protectedText);
        var protectedPlan=new RefinementContractPlan(protectedLedger.Spans.Select(s=>new RefinementContractSection(s.Kind,[s.Id])).ToList());
        var protectedReview=RefinementContract.Compose(protectedLedger,protectedPlan);
        c.Check(!protectedReview.Valid||!protectedReview.Useful||(protectedReview.Text.Contains("`x != 7`",StringComparison.Ordinal)&&protectedReview.Text.Contains("\"Do not publish\"",StringComparison.Ordinal)&&protectedReview.Text.Contains("2026-10-05",StringComparison.Ordinal)&&protectedReview.Text.Contains("😀",StringComparison.Ordinal)),"GRAMMAR-LITERALS","Code, quoted negation, exact number/date and Unicode survive or the structure is refused.");
        var ids=plan.Sections.SelectMany(s=>s.SourceIds).ToArray();
        var duplicated=new RefinementContractPlan(plan.Sections.Select(s=>new RefinementContractSection(s.Kind,s.SourceIds.ToList())).ToList()); duplicated.Sections[0].SourceIds.Add(ids[0]);
        c.Check(!RefinementContract.Compose(ledger,duplicated).Valid,"GRAMMAR-COVERAGE","Article repair cannot authorize duplicated source occurrences.");
        var reOrdered=new RefinementContractPlan(plan.Sections.AsEnumerable().Reverse().ToList());
        c.Check(!RefinementContract.Compose(ledger,reOrdered).Valid,"GRAMMAR-ORDER","Presentation cannot reorder the source roles.");
    }
}
#endif
