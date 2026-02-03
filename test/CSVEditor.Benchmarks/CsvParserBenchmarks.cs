using BenchmarkDotNet.Attributes;
using CSVEditor.Core;
using System.Text;
using Microsoft.VSDiagnostics;

namespace CSVEditor.Benchmarks;
[CPUUsageDiagnoser]
public class CsvParserBenchmarks
{
    private string _smallCsv = default!;
    private string _mediumCsv = default!;
    private string _largeCsv = default!;
    private string _quotedCsv = default!;
    private string _singleLine = default!;
    [GlobalSetup]
    public void Setup()
    {
        // Small CSV: 10 rows, 5 columns
        _smallCsv = GenerateCsv(10, 5);
        // Medium CSV: 100 rows, 10 columns
        _mediumCsv = GenerateCsv(100, 10);
        // Large CSV: 1000 rows, 20 columns
        _largeCsv = GenerateCsv(1000, 20);
        // CSV with quoted fields containing special characters
        _quotedCsv = GenerateQuotedCsv(100, 10);
        // Single line for ParseLine benchmark
        _singleLine = "Column1,Column2,Column3,\"Quoted Value\",Column5,Column6,Column7,Column8,Column9,Column10";
    }

    private static string GenerateCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                if (col > 0)
                    sb.Append(',');
                sb.Append($"Value_{row}_{col}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string GenerateQuotedCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                if (col > 0)
                    sb.Append(',');
                if (col % 3 == 0)
                {
                    // Quoted field with embedded quotes and commas
                    sb.Append($"\"Value with \"\"quotes\"\" and, commas {row}_{col}\"");
                }
                else
                {
                    sb.Append($"Value_{row}_{col}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    [Benchmark]
    public CsvDocument ParseSmallCsv()
    {
        return CsvParser.Parse(_smallCsv, ',');
    }

    [Benchmark]
    public CsvDocument ParseMediumCsv()
    {
        return CsvParser.Parse(_mediumCsv, ',');
    }

    [Benchmark]
    public CsvDocument ParseLargeCsv()
    {
        return CsvParser.Parse(_largeCsv, ',');
    }

    [Benchmark]
    public CsvDocument ParseQuotedCsv()
    {
        return CsvParser.Parse(_quotedCsv, ',');
    }

    [Benchmark]
    public CsvRow ParseSingleLine()
    {
        return CsvParser.ParseLine(_singleLine, ',', 0, 0);
    }
}