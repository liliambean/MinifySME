using System.Reflection.Emit;

var spriteFolderPath = @"C:\Users\Fred\Documents\Git\s3unlocked\General\Sprites";
var players = new[]
{
    "Sonic",
    "Tails",
    "Knuckles",
};

foreach (var player in players)
{
    OptimizePlayerMappings(player);
}

void OptimizePlayerMappings(string player) {
    var targetMapPath = $"{spriteFolderPath}\\{player}\\Map - {player}.asm";
    var targetPLCPath = $"{spriteFolderPath}\\{player}\\DPLC - {player}.asm";
    var sourceMap = ParseFile($"{spriteFolderPath}\\{player}\\map.asm", 13);
    var sourcePLC = ParseFile($"{spriteFolderPath}\\{player}\\plc.asm", 5);
    var targetMap = ParseFile(targetMapPath, 13);
    var targetPLC = ParseFile(targetPLCPath, 5);

    WriteFile(sourceMap, targetMap, targetMapPath, true);
    WriteFile(sourcePLC, targetPLC, targetPLCPath, false);
}

void WriteFile(ParseResult source, ParseResult target, string path, bool byteMode)
{
    var sourceDefinitions = SortDefinitions(source);
    var targetDefinitions = SortDefinitions(target);
}

ParseResult ParseFile(string path, int bufferSize)
{
    var offsetTables = new Dictionary<string, IList<string>>();
    var definitions = new Dictionary<short[], IList<string>>(new DefinitionComparer());
    var buffer = new List<short>(bufferSize);
    var label = string.Empty;
    var tableMode = true;

    using var reader = File.OpenText(path);

    while (reader.ReadLine() is string read)
    {
        if (string.IsNullOrWhiteSpace(read))
            continue;

        read = read.Split(';', 2)[0].TrimEnd();
        var line = read.TrimStart();

        if (string.IsNullOrWhiteSpace(line))
            continue;

        if (line.Length == read.Length)
        {
            var definition = buffer.ToArray();
            buffer = new List<short>(bufferSize);

            if (definitions.TryGetValue(definition, out var labels))
            {
                if (labels.Contains(label))
                    throw new InvalidDataException(label);

                labels.Add(label);
            }
            else if (offsetTables.Values.Any(table => table.Contains(label)))
            {
                definitions.Add(definition, new List<string> { label });
            }

            var split = line.Split(':', 2);
            // TODO: handle multiple labels in a row
            label = split[0];
            line = split[1].TrimStart();

            if (tableMode && offsetTables.Values.Any(table => table.Contains(label)))
            {
                tableMode = false;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;
        }

        if (line == "even")
            break;

        var tokens = line.Split(null, 2);
        var byteMode = tokens[0] switch
        {
            "dc.b" => true,
            "dc.w" => false,
            _ => throw new InvalidDataException(label)
        };

        if (tableMode)
        {
            if (byteMode)
                throw new InvalidDataException(label);

            tokens = tokens[1].Split('-', 2);
            var value = tokens[0];
            var tableName = tokens[1];

            if (offsetTables.TryGetValue(tableName, out var table))
            {
                if (table.Contains(value))
                {
                    var mainTable = offsetTables.First().Value;

                    if (table == mainTable || value != mainTable.First())
                        throw new InvalidDataException(value);
                }

                table.Add(value);
            }
            else
            {
                offsetTables.Add(tableName, new List<string> { value });
            }
        }
        else
        {
            var values = tokens[1].Split(',', StringSplitOptions.TrimEntries)
                .AsEnumerable().GetEnumerator();

            while (values.MoveNext())
            {
                if (byteMode)
                {
                    var high = (short)(ParseValue(values.Current) << 8);
                    if (!values.MoveNext())
                        throw new InvalidDataException(label);

                    buffer.Add((short)(ParseValue(values.Current) | high));
                }
                else
                {
                    buffer.Add(ParseValue(values.Current));
                }
            }

        }
    }

    return new ParseResult(offsetTables, definitions);
}

short ParseValue(string value) {
    return !value.StartsWith('$') ? short.Parse(value)
        : short.Parse(value[1..], System.Globalization.NumberStyles.AllowHexSpecifier);
}

IList<KeyValuePair<short[], IList<string>>> SortDefinitions(ParseResult result) => result
    .Definitions.OrderBy(definition => definition.Value, new TableEntryComparer(result)).ToList();

record ParseResult(
    IReadOnlyDictionary<string, IList<string>> OffsetTables,
    IReadOnlyDictionary<short[], IList<string>> Definitions
);

class TableEntryComparer : IComparer<IList<string>>
{
    public TableEntryComparer(ParseResult result)
    {

    }

    public int Compare(IList<string>? a, IList<string>? b)
    {
        throw new NotImplementedException();
    }
}

class DefinitionComparer : IEqualityComparer<short[]>
{
    public bool Equals(short[]? a, short[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a!.Length != b!.Length)
            return false;

        for (var index = 0; index < a.Length; index++)
        {
            if (a[index] != b[index])
                return false;
        }

        return true;
    }

    public int GetHashCode(short[] words)
    {
        if (words is null)
            return 0;

        var acc = 0;
        var high = false;

        foreach (var word in words)
        {
            acc ^= high? word << 16 : word;
            high = !high;
        }

        return acc;
    }
}
