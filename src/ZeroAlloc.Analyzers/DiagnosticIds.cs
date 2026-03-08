namespace ZeroAlloc.Analyzers;

public static class DiagnosticIds
{
    public const string UseFrozenDictionary = "ZA0001";
    public const string UseFrozenSet = "ZA0002";
    public const string UseCollectionsMarshalAsSpan = "ZA0003";
    public const string UseSearchValues = "ZA0004";
    public const string AvoidStringConcatInLoop = "ZA0005";
    public const string UseStackalloc = "ZA0006";
    public const string UseArrayPool = "ZA0007";
    public const string AvoidEnumHasFlag = "ZA0008";
    public const string AvoidStringReplaceChain = "ZA0009";
    public const string UseTryGetValue = "ZA0010";
}
