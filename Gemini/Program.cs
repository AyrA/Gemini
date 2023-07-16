using Gemini;

//URLs in the order of the tests
var urls = new string[]
{
    "gemini://gemini.conman.org/test/torture/",
    "gemini://gemini.thebackupbox.net/test/torture/",
    "gemini://gemini.thebackupbox.net/test/torture/0001",
    "gemini://gemini.thebackupbox.net/test/torture/0002",
    "//gemini.thebackupbox.net/test/torture/0003",
    "//gemini.thebackupbox.net/test/torture/0004",
    "/test/torture/0005",
    "/test/torture/0006",
    "0007",
    "0008",
    "/test/../test/torture/0009",
    "/test/torture/../../test/./torture/0010",
    "../../../../test/./././torture/./0011",
    "0012",
    "0013",
    "0014",
    "0015",
    "0016",
    "0017",
    "0018",
    "0019",
    "0020",
    "0021", //Explore this code
    //Redirect hell
        "0022",
        //"/test/redirhell/1234",
        "0023",
        "0024",
        "0025",
        "0026",
        "0027", //Other protocol
    // text/gemini tests
        "0028",
        "0029",
        "0030",
        "0031",
        "<0032>",
        "",
        "0033",
    //Unknown or invalid status codes
        "0034",
        "0034a",
        "0035",
        "0035a",
        "0036",
        "0036a",
        "0037",
        "0037a",
        "0038",
        "0038a",
        "0039",
        "0039a",
        "0040",
        "0040a",
    //Text handling
        "0041",
        "0042",
        "0043",
        "0044",
        "0045",
        "0046",
        "0047",
        "0048",
        "0049",
        "0050",
        "0051",
        "//gemini.circumlunar.space/docs/"
};

var url = new Uri(urls[0]);
for (var i = 1; i < urls.Length; i++)
{
    url = new Uri(url, urls[i]);
}
do
{
    Console.Clear();
    await ContentRetriever.GetContentAsync(url, 10);
    Console.Write("Enter new URL: ");
    var line = Console.ReadLine();
    if (string.IsNullOrEmpty(line))
    {
        break;
    }
    url = new Uri(url, line);
} while (true);
