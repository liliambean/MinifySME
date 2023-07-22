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
    var sourceMap = Parse($"{spriteFolderPath}\\{player}\\map.asm", 3);
    var sourcePLC = Parse($"{spriteFolderPath}\\{player}\\plc.asm", 1);
    var targetMap = Parse(targetMapPath, 3);
    var targetPLC = Parse(targetPLCPath, 1);

    Write(sourceMap, targetMap, targetMapPath, true);
    Write(sourcePLC, targetPLC, targetPLCPath, false);
}

void Write(ParseResult source, ParseResult target, string path, bool byteMode)
{
    var definitions = SortDefinitions(source);
}

ParseResult Parse(string path, int entrySize)
{
    var mainTable = default(IList<string>);
    var offsetTables = new Dictionary<string, IList<string>>();
    var definitions = new Dictionary<IList<short>, HashSet<string>>(new DefinitionComparer());
    var labels = new HashSet<string>();
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
            var tokens = line.Split(':', 2);
            var label = tokens[0];

            if (definitions.Values.Any(existingLabels => existingLabels.Contains(label)))
                throw new InvalidDataException();

            if (tableMode && offsetTables.Values.Any(table => table.Contains(label)))
            {
                tableMode = false;
                labels.Clear();
            }

            labels.Add(label);
            line = tokens[1].TrimStart();

            if (string.IsNullOrWhiteSpace(line))
                continue;
        }

        if (line == "even")
            break;

        if (tableMode)
        {
            var tokens = line.Split(null, 2);
            if (GetOperandSize(tokens[0])) throw new InvalidDataException();

            tokens = tokens[1].Split('-', 2);
            var (label, tableName) = (tokens[0], tokens[1]);

            if (!offsetTables.TryGetValue(tableName, out var table))
            {
                table = new List<string> { label };
                offsetTables.Add(tableName, table);
                mainTable ??= table;
            }
            else if (table != mainTable && label != mainTable!.First() && table.Contains(label))
                throw new InvalidDataException();
            else
                table.Add(label);
        }
        else
        {
            (var operands, var operandSize) = GetOperandEnumerator(line);
            (operands, operandSize, var definition) = GetWords(reader, operands, operandSize, 1);
            var numWords = definition[0] * entrySize;

            if (numWords != 0)
                definition.AddRange(GetWords(reader, operands, operandSize, numWords).Words);

            if (!definitions.TryGetValue(definition, out var existingLabels))
            {
                definitions.Add(definition, labels);
                labels = new HashSet<string>();
            }
            else
            {
                foreach (var label in labels) existingLabels.Add(label);
                labels.Clear();
            }
        }

    }

    return new ParseResult(offsetTables, definitions);
}

(IEnumerator<string>, bool, List<short> Words) GetWords(StreamReader reader, IEnumerator<string> operands, bool operandSizeIsByte, int count)
{
    var words = new List<short>(count);
    var bytes = new List<byte>(2);

    while (true)
    {
        if (operandSizeIsByte)
        {
            while (operands.MoveNext())
            {
                bytes.Add((byte)GetOperandValue(operands.Current));
                if (bytes.Count == 2)
                {
                    words.Add((short)(bytes[0] << 8 | bytes[1]));
                    if (words.Count == count) return (operands, operandSizeIsByte, words);
                    bytes.Clear();
                }
            }
        }
        else
        {
            if (bytes.Count != 0) throw new InvalidDataException();

            while (operands.MoveNext())
            {
                words.Add((short)GetOperandValue(operands.Current));
                if (words.Count == count) return (operands, operandSizeIsByte, words);
            }
        }

        var line = reader.ReadLine() ?? throw new InvalidDataException();
        (operands, operandSizeIsByte) = GetOperandEnumerator(line.TrimStart());
    }
}

(IEnumerator<string>, bool) GetOperandEnumerator(string line)
{
    var tokens = line.Split(null, 2);
    var operandSizeIsByte = GetOperandSize(tokens[0]);
    var operands = tokens[1]
        .Split(',', StringSplitOptions.TrimEntries).AsEnumerable().GetEnumerator();

    return (operands, operandSizeIsByte);
}

bool GetOperandSize(string token) => token switch
{
    "dc.b" => true,
    "dc.w" => false,
    _ => throw new InvalidDataException(token)
};

int GetOperandValue(string value) {
    return !value.StartsWith('$') ? int.Parse(value) : 
        int.Parse(value[1..], System.Globalization.NumberStyles.AllowHexSpecifier);
}

IList<KeyValuePair<IList<short>, HashSet<string>>> SortDefinitions(ParseResult result) => result
    .Definitions.OrderBy(definition => definition.Value, new TableEntryComparer(result)).ToList();

record ParseResult(
    IReadOnlyDictionary<string, IList<string>> OffsetTables,
    IReadOnlyDictionary<IList<short>, HashSet<string>> Definitions
);

class TableEntryComparer : IComparer<HashSet<string>>
{
    public TableEntryComparer(ParseResult result)
    {

    }

    public int Compare(HashSet<string>? a, HashSet<string>? b)
    {
        return 0;
    }
}

class DefinitionComparer : IEqualityComparer<IList<short>>
{
    public bool Equals(IList<short>? a, IList<short>? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a!.Count != b!.Count)
            return false;

        for (var index = 0; index < a.Count; index++)
        {
            if (a[index] != b[index])
                return false;
        }

        return true;
    }

    public int GetHashCode(IList<short> words)
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