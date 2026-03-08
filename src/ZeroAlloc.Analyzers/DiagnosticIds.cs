namespace ZeroAlloc.Analyzers;

public static class DiagnosticIds
{
    public const string UseFrozenDictionary = "NP0001";
    public const string UseFrozenSet = "NP0002";
    public const string UseCollectionsMarshalAsSpan = "NP0003";
    public const string UseSearchValues = "NP0004";
    public const string AvoidStringConcatInLoop = "NP0005";
    public const string UseStackalloc = "NP0006";
    public const string UseArrayPool = "NP0007";
    public const string AvoidEnumHasFlag = "NP0008";
    public const string AvoidStringReplaceChain = "NP0009";
    public const string UseTryGetValue = "NP0010";
}
