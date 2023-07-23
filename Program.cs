﻿namespace SpriteMapOptimizer
{
    using System.Collections.Immutable;
    using System.Text;

    class Program
    {
        static readonly string spriteFolderPath = @"C:\Users\Fred\Documents\Git\s3unlocked\General\Sprites";
        static readonly string[] players = new[]
        {
            "Sonic",
            "Tails",
            "Knuckles",
        };

        static void Main()
        {
            foreach (var player in players)
            {
                var targetMapPath = $"{spriteFolderPath}\\{player}\\Map - {player}.asm";
                var targetPLCPath = $"{spriteFolderPath}\\{player}\\DPLC - {player}.asm";

                var sourceMap = Parse($"{spriteFolderPath}\\{player}\\map.asm", 3);
                var sourcePLC = Parse($"{spriteFolderPath}\\{player}\\plc.asm", 1);
                var targetMap = Parse(targetMapPath, 3);
                var targetPLC = Parse(targetPLCPath, 1);

                var map = Serialize(sourceMap, targetMap, true);
                var plc = Serialize(sourcePLC, targetPLC, false);
                File.WriteAllText(targetMapPath, map);
                File.WriteAllText(targetPLCPath, plc);
            }
        }

        static string Serialize(ParseResult source, ParseResult target, bool byteMode)
        {
            var builder = new StringBuilder();
            var definitions = SortDefinitions(source);

            var offsetTable = target.OffsetTable
                .GroupBy(entry => entry.TableName)
                .ToDictionary(group => group.Key, group => group.ToList())
                .SelectMany(group => group.Value, (_, entry) => (entry.Label, entry.TableName))
                .GetEnumerator();

            offsetTable.MoveNext();
            var prevTable = offsetTable.Current.TableName;

            do
            {
                var (label, tableName) = offsetTable.Current;

                if (prevTable != tableName)
                {
                    prevTable = tableName;
                    builder.AppendLine($"{tableName}:");
                }

                builder.AppendLine($"\t\tdc.w {label}-{tableName}");

            } while (offsetTable.MoveNext());

            foreach (var (definition, indices) in definitions)
            {
                var labels = indices.Select(index => target.OffsetTable[index].Label)
                    .ToImmutableSortedSet();

                foreach (var label in labels)
                    builder.AppendLine($"{label}:");

                builder.AppendLine($"\t\tdc.w {SerializeOperandValue(definition.First())}");

                if (byteMode)
                {
                    var values = definition.Skip(1).SelectMany(WordToBytes);
                    while (values.Any())
                    {
                        var entry = string.Join(',', values.Take(6).Select(SerializeOperandValue));
                        builder.AppendLine($"\t\tdc.b {entry}");
                        values = values.Skip(6);
                    }
                }
                else
                {
                    foreach (var value in definition.Skip(1))
                        builder.AppendLine($"\t\tdc.w {SerializeOperandValue(value)}");
                }
            }

            return builder.ToString();
        }

        static ParseResult Parse(string path, int entrySize)
        {
            var labels = new HashSet<string>();
            var definitions = new Dictionary<IList<short>, HashSet<string>>(new DefinitionComparer());
            var offsetTable = new List<(string Label, string TableName)>();
            var mainTable = default(string);
            var tableMode = true;

            using var reader = File.OpenText(path);

            while (reader.ReadLine() is string read)
            {
                if (string.IsNullOrWhiteSpace(read))
                    continue;

                read = StripComments(read).TrimEnd();
                var line = read.TrimStart();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.Length == read.Length)
                {
                    var tokens = line.Split(':', 2);
                    var label = tokens[0];

                    if (definitions.Values.Any(existingLabels => existingLabels.Contains(label)))
                        throw new InvalidDataException();

                    if (tableMode && offsetTable.Any(entry => entry.Label == label))
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

                var (operands, operandSize) = GetOperandEnumerator(line);

                if (tableMode)
                {
                    if (operandSize) throw new InvalidDataException();

                    while (operands.MoveNext())
                    {
                        var tokens = operands.Current.Split('-', 2);
                        var entry = (tokens[0], tokens[1]);
                        var (label, tableName) = entry;

                        if (mainTable is null)
                        {
                            mainTable = tableName;
                        }
                        else if ((tableName == mainTable || label != offsetTable.First().Label) &&
                            offsetTable.Any(entry => entry.Label == label && entry.TableName == tableName))
                        {
                            throw new InvalidDataException();
                        }

                        offsetTable.Add(entry);
                    }
                }
                else
                {
                    (operands, operandSize, var definition) = ParseWords(reader, operands, operandSize, 1);
                    var numWords = definition[0] * entrySize;

                    if (numWords != 0)
                        definition.AddRange(ParseWords(reader, operands, operandSize, numWords).Words);

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

            return new ParseResult(offsetTable, definitions);
        }

        static string StripComments(string line)
        {
            return line.Split(';', 2)[0];
        }

        static (IEnumerator<string>, bool) GetOperandEnumerator(string line)
        {
            var tokens = line.Split(null, 2);
            var operandSizeIsByte = ParseOperandSize(tokens[0]);
            var operands = tokens[1]
                .Split(',', StringSplitOptions.TrimEntries).AsEnumerable().GetEnumerator();

            return (operands, operandSizeIsByte);
        }

        static (IEnumerator<string>, bool, List<short> Words)
        ParseWords(StreamReader reader, IEnumerator<string> operands, bool operandSizeIsByte, int count)
        {
            var words = new List<short>(count);
            var bytes = new List<byte>(2);

            while (true)
            {
                if (operandSizeIsByte)
                {
                    while (operands.MoveNext())
                    {
                        bytes.Add((byte)ParseOperandValue(operands.Current));
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
                        words.Add((short)ParseOperandValue(operands.Current));
                        if (words.Count == count) return (operands, operandSizeIsByte, words);
                    }
                }

                var line = reader.ReadLine() ?? throw new InvalidDataException();
                (operands, operandSizeIsByte) = GetOperandEnumerator(StripComments(line).Trim());
            }
        }

        static bool ParseOperandSize(string token) => token switch
        {
            "dc.b" => true,
            "dc.w" => false,
            _ => throw new InvalidDataException(token)
        };

        static int ParseOperandValue(string token)
        {
            return !token.StartsWith('$') ? int.Parse(token) :
                int.Parse(token[1..], System.Globalization.NumberStyles.AllowHexSpecifier);
        }

        static string SerializeOperandValue(byte value)
        {
            var hex = value.ToString("X");
            return (value.ToString().Equals(hex) ? hex : $"${hex}").PadLeft(4);
        }

        static string SerializeOperandValue(short value)
        {
            var hex = value.ToString("X");
            return value.ToString().Equals(hex) ? hex : $"${hex}";
        }

        static IEnumerable<byte> WordToBytes(short value)
        {
            yield return (byte)(value >> 8);
            yield return (byte)value;
            yield break;
        }

        static IList<(IList<short>, IList<int>)> SortDefinitions(ParseResult result)
        {
            var labels = result.OffsetTable.Select(entry => entry.Label).ToList();

            return result.Definitions
                .Select(definition => (definition.Key, definition.Value
                    .Select(label => labels.IndexOf(label))
                    .OrderBy(index => index)
                    .ToList() as IList<int>
                ))
                .OrderBy(definition => definition.Item2.First()).ToList();
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
                acc ^= high ? word << 16 : word;
                high = !high;
            }

            return acc;
        }
    }

    record ParseResult(
        IList<(string Label, string TableName)> OffsetTable,
        IReadOnlyDictionary<IList<short>, HashSet<string>> Definitions
    );
}
