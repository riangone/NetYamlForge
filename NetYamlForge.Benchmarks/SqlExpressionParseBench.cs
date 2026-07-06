using BenchmarkDotNet.Attributes;
using NetYamlForge.Services;

namespace NetYamlForge.Benchmarks;

[MemoryDiagnoser]
public class SqlExpressionParseBench
{
    private const string SimpleExpression = "Amount * 2";
    private const string ComplexExpression = "COALESCE(LOWER(Name), 'unknown')";

    [Benchmark]
    public void ParseSimpleExpression()
    {
        SqlExpressionParser.Validate(SimpleExpression, "bench");
    }

    [Benchmark]
    public void ParseComplexExpression()
    {
        SqlExpressionParser.Validate(ComplexExpression, "bench");
    }
}
