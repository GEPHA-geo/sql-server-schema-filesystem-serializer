namespace SqlServerStructureGenerator;

// List of tables that are known to fail scripting due to SMO issues
public static class ProblematicTables
{
    // Tables that fail with "Failed to retrieve data for this request" error
    public static readonly HashSet<string> SkipTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "tanamsr_avansi",
        "tanamsr_cvl",
        "tanamsr_gany",
        "tanamsr_monitor",
        "tanamsr_paroli",
        "tanamsr_sq",
        "tanamsr_superv",
        "tanamsr_sveb",
        "tanamsr_tanam",
        "tanamsr_xelf",
        "terminal_app_proc",
        "terminal_arwera",
        "terminal_contr",
        "test_sakitxebi",
        "test_tema",
        "test_testireba",
        "test_testireba_det",
        "test_testireba_tanxmoba",
        "valuta",
        "valuta1",
        "x_triger_error",
        "xelsekruleba",
        "xx"
    };
    
    public static bool ShouldSkip(string tableName) => SkipTables.Contains(tableName);
}