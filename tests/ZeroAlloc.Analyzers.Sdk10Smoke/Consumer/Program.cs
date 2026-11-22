// ZA0105 reports ContainsKey followed by the indexer on the same key. The report only exists if
// the compiler loaded the analyzer; check.sh looks for it in the build output.
var prices = new Dictionary<string, decimal> { ["apple"] = 1.5m };

Console.WriteLine(Lookup(prices, "apple"));
return 0;

static decimal Lookup(Dictionary<string, decimal> prices, string key)
{
    if (prices.ContainsKey(key))
    {
        return prices[key];
    }

    return 0m;
}
