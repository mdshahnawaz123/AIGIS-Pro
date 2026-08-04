using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace ValidatorApp;

public static class Program
{
    public static void Main()
    {
        string outputJson = Path.GetFullPath(@"..\..\tests\AiGisConverter.IntegrationTests\bin\Debug\net8.0\out_e2e\Unclassified.geojson");
        var geojson = File.ReadAllText(outputJson);
        var doc = JsonDocument.Parse(geojson);
        
        string crs = "Not specified";
        if (doc.RootElement.TryGetProperty("crs", out var crsProp) &&
            crsProp.TryGetProperty("properties", out var crsProps) &&
            crsProps.TryGetProperty("name", out var crsName))
        {
            crs = crsName.GetString();
        }
        Console.WriteLine($"CRS: {crs}");
        
        var features = doc.RootElement.GetProperty("features");
        int validGeometries = 0;
        int nullGeometries = 0;
        Dictionary<string, int> geomTypes = new Dictionary<string, int>();
        HashSet<string> properties = new HashSet<string>();
        
        foreach (var feature in features.EnumerateArray())
        {
            var geom = feature.GetProperty("geometry");
            if (geom.ValueKind == JsonValueKind.Null)
            {
                nullGeometries++;
            }
            else
            {
                validGeometries++;
                var type = geom.GetProperty("type").GetString();
                if (type != null)
                {
                    if (!geomTypes.ContainsKey(type))
                    {
                        geomTypes[type] = 0;
                    }
                    geomTypes[type]++;
                }
            }
            
            var props = feature.GetProperty("properties");
            foreach (var prop in props.EnumerateObject())
            {
                properties.Add(prop.Name);
            }
        }
        
        Console.WriteLine($"Total Features: {features.GetArrayLength()}");
        Console.WriteLine($"Valid Geometries: {validGeometries}, Null Geometries: {nullGeometries}");
        foreach(var kv in geomTypes)
        {
            Console.WriteLine($"Geometry Type {kv.Key}: {kv.Value}");
        }
        Console.WriteLine($"Properties: {string.Join(", ", properties)}");
    }
}
