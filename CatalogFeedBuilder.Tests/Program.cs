using System.Xml.Linq;
using CatalogFeedBuilder;

var csv = "sku,name,category,price,currency,url,description\n" +
          "A1,Camera,Electronics,129.90,RUB,https://example.test/a1,Compact\n" +
          "A2,\"Tripod, 2m\",Accessories,30,RUB,https://example.test/a2,\"Steel\nframe\"\n" +
          "A1,Duplicate,Electronics,100,RUB,https://example.test/a3,Duplicate\n" +
          "B1,Bad price,Electronics,0,RUB,https://example.test/b1,Invalid\n" +
          "C1,Bad URL,Electronics,10,RUB,javascript:alert(1),Invalid\n";

var result = CatalogConverter.Import(new StringReader(csv));
Check(result.Products.Count == 2, "Должны быть приняты две строки");
Check(result.Issues.Count == 3, "Должны быть найдены три ошибки");
Check(result.Issues.Select(x => x.Row).SequenceEqual([5, 6, 7]), "Номера строк с многострочным CSV");
Check(result.Products[1].Name == "Tripod, 2m", "Кавычки и запятая в CSV");
Check(result.Products[1].Description == "Steel\nframe", "Многострочное поле CSV");

var xmlText = new StringWriter();
using (var xml = System.Xml.XmlWriter.Create(xmlText))
    CatalogConverter.WriteYml(xml, result, "Demo & Shop", "Company", "https://example.test");
var doc = XDocument.Parse(xmlText.ToString());
Check(doc.Root?.Name == "yml_catalog", "Корневой элемент YML");
Check(doc.Descendants("offer").Count() == 2, "В YML не попали ошибочные товары");
Check(doc.Descendants("shop").Single().Element("name")?.Value == "Demo & Shop", "XML-экранирование");
Check(doc.Descendants("category").Count() == 2, "Категории созданы");

var html = CatalogConverter.BuildReport(result, "feed.yml");
Check(html.Contains("Принято товаров"), "Отчёт сформирован");
Check(!html.Contains("javascript:alert(1)"), "Небезопасный URL не попал в отчёт");
try { CatalogConverter.Import(new StringReader("sku,name\nA,B\n")); throw new Exception("Ожидалась ошибка заголовка"); }
catch (InvalidDataException) { }

Console.WriteLine("Все проверки пройдены.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("Провал: " + message);
}
