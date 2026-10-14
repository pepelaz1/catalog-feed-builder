using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;

namespace CatalogFeedBuilder;

public sealed record Product(string Sku, string Name, string Category, decimal Price,
    string Currency, string Url, string Description);

public sealed record ImportIssue(int Row, string Message);

public sealed record ImportResult(IReadOnlyList<Product> Products, IReadOnlyList<ImportIssue> Issues);

public static class CatalogConverter
{
    private static readonly string[] RequiredHeaders =
        ["sku", "name", "category", "price", "currency", "url", "description"];

    public static ImportResult Import(TextReader reader)
    {
        var records = ReadCsv(reader).ToList();
        if (records.Count == 0)
            throw new InvalidDataException("CSV-файл пуст.");

        var headers = records[0].Cells.Select(x => x.Trim().ToLowerInvariant()).ToArray();
        var missing = RequiredHeaders.Except(headers).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("Нет обязательных столбцов: " + string.Join(", ", missing));
        if (headers.Distinct().Count() != headers.Length)
            throw new InvalidDataException("В заголовке есть повторяющиеся столбцы.");

        var positions = RequiredHeaders.ToDictionary(x => x, x => Array.IndexOf(headers, x));
        var seenSku = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var products = new List<Product>();
        var issues = new List<ImportIssue>();

        foreach (var (row, cells) in records.Skip(1))
        {
            if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0])) continue;
            if (cells.Count != headers.Length)
            {
                issues.Add(new(row, $"Ожидалось {headers.Length} столбцов, получено {cells.Count}."));
                continue;
            }

            string Get(string key) => cells[positions[key]].Trim();
            var sku = Get("sku");
            var name = Get("name");
            var category = Get("category");
            var currency = Get("currency").ToUpperInvariant();
            var url = Get("url");
            var description = Get("description");

            if (sku.Length == 0 || name.Length == 0 || category.Length == 0)
            {
                issues.Add(new(row, "Артикул, название и категория обязательны."));
                continue;
            }
            if (seenSku.Contains(sku))
            {
                issues.Add(new(row, $"Повтор артикула: {sku}."));
                continue;
            }
            if (!decimal.TryParse(Get("price"), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var price) || price <= 0)
            {
                issues.Add(new(row, "Цена должна быть положительным числом с точкой."));
                continue;
            }
            if (currency is not ("RUB" or "USD" or "EUR"))
            {
                issues.Add(new(row, "Поддерживаемые валюты: RUB, USD, EUR."));
                continue;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) ||
                parsedUrl.Scheme is not ("https" or "http"))
            {
                issues.Add(new(row, "URL должен начинаться с http:// или https://."));
                continue;
            }

            seenSku.Add(sku);
            products.Add(new(sku, name, category, price, currency, url, description));
        }

        return new(products, issues);
    }

    public static void WriteYml(XmlWriter writer, ImportResult import,
        string shopName, string companyName, string shopUrl)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("yml_catalog");
        writer.WriteAttributeString("date", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        writer.WriteStartElement("shop");
        writer.WriteElementString("name", shopName);
        writer.WriteElementString("company", companyName);
        writer.WriteElementString("url", shopUrl);

        writer.WriteStartElement("currencies");
        foreach (var currency in import.Products.Select(x => x.Currency).Distinct().OrderBy(x => x))
        {
            writer.WriteStartElement("currency");
            writer.WriteAttributeString("id", currency);
            writer.WriteAttributeString("rate", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        var categories = import.Products.Select(x => x.Category).Distinct()
            .OrderBy(x => x, StringComparer.Ordinal).Select((name, index) => (name, id: index + 1))
            .ToDictionary(x => x.name, x => x.id, StringComparer.Ordinal);
        writer.WriteStartElement("categories");
        foreach (var (name, id) in categories)
        {
            writer.WriteStartElement("category");
            writer.WriteAttributeString("id", id.ToString(CultureInfo.InvariantCulture));
            writer.WriteString(name);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteStartElement("offers");
        foreach (var product in import.Products)
        {
            writer.WriteStartElement("offer");
            writer.WriteAttributeString("id", product.Sku);
            writer.WriteAttributeString("available", "true");
            writer.WriteElementString("url", product.Url);
            writer.WriteElementString("price", product.Price.ToString("0.##", CultureInfo.InvariantCulture));
            writer.WriteElementString("currencyId", product.Currency);
            writer.WriteElementString("categoryId", categories[product.Category].ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("name", product.Name);
            if (product.Description.Length != 0)
                writer.WriteElementString("description", product.Description);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    public static string BuildReport(ImportResult import, string outputName)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var rows = new StringBuilder();
        foreach (var product in import.Products)
            rows.Append($"<tr><td>{E(product.Sku)}</td><td>{E(product.Name)}</td><td>{E(product.Category)}</td><td>{product.Price.ToString("0.##", CultureInfo.InvariantCulture)} {E(product.Currency)}</td><td><span class='badge ok'>Принят</span></td></tr>");
        foreach (var issue in import.Issues)
            rows.Append($"<tr class='bad'><td>Строка {issue.Row}</td><td colspan='3'>{E(issue.Message)}</td><td><span class='badge error'>Ошибка</span></td></tr>");
        return $$$"""
            <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Catalog Feed Builder — отчёт импорта</title>
            <style>
            :root{font-family:Inter,Segoe UI,Arial,sans-serif;color:#162234;background:#f4f7fb}*{box-sizing:border-box}
            body{margin:0}header{background:#142b50;color:white;padding:26px max(24px,calc((100vw - 1100px)/2))}
            header small{color:#9fc4ff;letter-spacing:.12em;text-transform:uppercase;font-weight:700}h1{margin:8px 0 4px;font-size:28px}header p{color:#d4e1f5;margin:0}
            main{max-width:1100px;margin:32px auto;padding:0 24px}.stats{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}
            .card,.panel{background:white;border:1px solid #e3e9f1;border-radius:14px;box-shadow:0 6px 20px #1831510c}
            .card{padding:22px}.card span{color:#64748b;font-size:14px}.card strong{display:block;font-size:30px;margin-top:8px}
            .card.good strong{color:#167a55}.card.warn strong{color:#b56523}.panel{margin-top:24px;overflow:hidden}
            .panelhead{display:flex;justify-content:space-between;align-items:center;padding:20px 24px;border-bottom:1px solid #e3e9f1}
            h2{font-size:19px;margin:0}.download{background:#2764d8;color:white;text-decoration:none;padding:10px 16px;border-radius:8px;font-size:14px;font-weight:600}
            table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:15px 24px;border-bottom:1px solid #edf1f6;font-size:14px}
            th{color:#64748b;background:#fafcff;font-size:12px;text-transform:uppercase;letter-spacing:.06em}tr:last-child td{border-bottom:0}
            .bad{background:#fffaf6}.badge{display:inline-block;border-radius:99px;padding:5px 10px;font-size:12px;font-weight:700}
            .ok{color:#14734f;background:#e7f8ef}.error{color:#a44920;background:#ffeadc}
            footer{margin:20px 0;color:#718096;font-size:13px}@media(max-width:650px){.stats{grid-template-columns:1fr}table{display:block;overflow:auto}.panelhead{gap:12px;flex-wrap:wrap}}
            </style></head><body><header><small>.NET 8 · CSV → YML</small><h1>Проверка товарного каталога</h1><p>Отчёт импорта и готовая XML/YML-выгрузка</p></header>
            <main><div class="stats"><div class="card"><span>Всего строк данных</span><strong>{{{import.Products.Count + import.Issues.Count}}}</strong></div>
            <div class="card good"><span>Принято товаров</span><strong>{{{import.Products.Count}}}</strong></div>
            <div class="card warn"><span>Ошибки в строках</span><strong>{{{import.Issues.Count}}}</strong></div></div>
            <section class="panel"><div class="panelhead"><h2>Результат проверки</h2><a class="download" href="{{{E(outputName)}}}" download>Скачать YML</a></div>
            <table><thead><tr><th>Артикул / строка</th><th>Название / причина</th><th>Категория</th><th>Цена</th><th>Статус</th></tr></thead><tbody>{{{rows}}}</tbody></table></section>
            <footer>Демонстрационные данные. Строки с ошибками не попадают в выгрузку.</footer></main></body></html>
            """;
    }

    private static IEnumerable<(int Row, List<string> Cells)> ReadCsv(TextReader reader)
    {
        var row = 1;
        var startRow = 1;
        var cells = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        var any = false;
        while (reader.Read() is var code && code >= 0)
        {
            var c = (char)code;
            any = true;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else { quoted = false; afterQuote = true; }
                }
                else
                {
                    if (c == '\n') row++;
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0 && !afterQuote) quoted = true;
            else if (c == ',')
            {
                cells.Add(field.ToString()); field.Clear(); afterQuote = false;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                cells.Add(field.ToString()); field.Clear(); afterQuote = false;
                yield return (startRow, cells); cells = new();
                row++; startRow = row; any = false;
            }
            else if (afterQuote && !char.IsWhiteSpace(c))
                throw new InvalidDataException($"Лишний символ после кавычек в строке {row}.");
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException($"Незакрытая кавычка в строке {startRow}.");
        if (any || cells.Count > 0)
        {
            cells.Add(field.ToString());
            yield return (startRow, cells);
        }
    }
}
