using System.Text;
using System.Xml;
using CatalogFeedBuilder;

if (args.Length < 2 || args.Length > 5)
{
    Console.Error.WriteLine("Использование: CatalogFeedBuilder <input.csv> <output.yml> [название магазина] [компания] [URL магазина]");
    return 2;
}

try
{
    var input = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    var shop = args.Length > 2 ? args[2] : "Демо-магазин";
    var company = args.Length > 3 ? args[3] : "Демо-компания";
    var url = args.Length > 4 ? args[4] : "https://example.test";
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        throw new ArgumentException("URL магазина должен начинаться с http:// или https://.");

    using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var import = CatalogConverter.Import(reader);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
    using (var xml = XmlWriter.Create(output, settings))
        CatalogConverter.WriteYml(xml, import, shop, company, url);

    var report = Path.ChangeExtension(output, ".html");
    File.WriteAllText(report, CatalogConverter.BuildReport(import, Path.GetFileName(output)), new UTF8Encoding(false));
    Console.WriteLine($"Принято: {import.Products.Count}; ошибок: {import.Issues.Count}.");
    foreach (var issue in import.Issues) Console.WriteLine($"Строка {issue.Row}: {issue.Message}");
    Console.WriteLine($"YML: {output}");
    Console.WriteLine($"Отчёт: {report}");
    return import.Issues.Count == 0 ? 0 : 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or XmlException)
{
    Console.Error.WriteLine("Ошибка: " + error.Message);
    return 2;
}
