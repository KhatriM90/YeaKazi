using System.Text;

namespace YeaKazi.People.Domain;

/// <summary>Minimal RFC 4180 CSV reader/writer (quoted fields, doubled quotes, CRLF or LF).</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
        {
            i = 1;
        }

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException("The file ends inside a quoted value.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        rows.RemoveAll(r => r.All(string.IsNullOrWhiteSpace));
        return rows;
    }

    public static string Write(IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendJoin(',', row.Select(Escape)).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;

        // Neutralise spreadsheet formulas (CSV injection) in values we write out.
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !(value[0] is '+' or '-' && value.Skip(1).All(ch => char.IsDigit(ch) || ch == '.')))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
