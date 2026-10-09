using System.Text.Json;

#if HAS_CONTRACT
SourceReceipt.Verify("independent meaning oracle and pure source-contract boundaries; no model or native calls");
#endif

int passed = 0, failed = 0;
void Check(bool condition, string name) {
    if (condition) passed++; else failed++;
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "oracle_case", name, status = condition ? "PASS" : "FAIL" }));
}
foreach (var golden in MeaningGoldens.All) {
    if (golden.CanStructure) Check(MeaningOracle.Judge(golden, golden.UsefulExample).MeetsGolden, golden.Id + ": human-authored useful example");
    Check(!MeaningOracle.Judge(golden, golden.Original).UsefulStructure, golden.Id + ": unchanged is not useful structure");
    foreach (string bad in golden.BadExamples) Check(!MeaningOracle.Judge(golden, bad).MeetsGolden, golden.Id + ": negative witness " + Array.IndexOf(golden.BadExamples, bad));
}
#if HAS_CONTRACT
ContractCases.Run(Check);
await ServiceCases.Run(Check);
#endif
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", passed, failed, scope = "independent fixture oracle; pure contract and mocked service when compiled with HAS_CONTRACT", realModel = "NOT RUN", native = "NOT RUN" }));
return failed == 0 ? 0 : 1;
