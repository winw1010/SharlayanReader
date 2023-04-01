using Newtonsoft.Json;
using Sharlayan;
using Sharlayan.Enums;
using Sharlayan.Models;
using System.Diagnostics;
using System.Text;

HttpPostModule httpPostModule = new HttpPostModule();
string[] cutsceneTextKeyArray = { "CUTSCENE_TEXT_1", "CUTSCENE_TEXT_2", "CUTSCENE_TEXT_3" };
//string[] panelNameKeyArray = { "PANEL_NAME_1" };

main();

void main()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;

        SharlayanConfiguration configuration = new SharlayanConfiguration
        {
            ProcessModel = new ProcessModel
            {
                Process = Process.GetProcessesByName("ffxiv_dx11").FirstOrDefault(),
            },
        };

        MemoryHandler memoryHandler = new MemoryHandler(configuration);
        // it be default will pull in the memory signatures from the local file, backup from API (GitHub)
        memoryHandler.Scanner.Locations.Clear(); // these are resolved MemoryLocation

        List<Signature> signatures = new List<Signature>();
        addSignature(signatures);

        memoryHandler.Scanner.LoadOffsets(signatures.ToArray());
        startFetching(memoryHandler, cutsceneTextKeyArray);
        //startFetching(memoryHandler, panelNameKeyArray);

        Console.WriteLine("讀取文字中，請勿關閉本視窗");
    }
    catch (Exception exception)
    {
        Console.WriteLine(exception.Message);
        Console.WriteLine("無法存取FFXIV，請在啟動FFXIV之後再啟動本程式\n按任意鍵關閉");
    }

    Console.ReadLine();
    return;
}

void addSignature(List<Signature> signatures)
{
    signatures.Add(new Signature
    {
        Key = "CUTSCENE_TEXT_1",
        PointerPath = new List<long>
            {
                0x020C9CC0,
                0x68,
                0x250,
                0x0
            }
    });

    signatures.Add(new Signature
    {
        Key = "CUTSCENE_TEXT_2",
        PointerPath = new List<long>
            {
                0x020AA1B0,
                0x10,
                0x20,
                0x100,
                0x0
            }
    });

    signatures.Add(new Signature
    {
        Key = "CUTSCENE_TEXT_3",
        PointerPath = new List<long>
            {
                0x020A4418,
                0x520,
                0x20,
                0x100,
                0x0
            }
    });

    /*
    signatures.Add(new Signature
    {
        Key = "PANEL_NAME_1",
        PointerPath = new List<long>
            {
                0x020C9CC0,
                0x148,
                0x0,
                0x10,
                0x10,
                0x10,
                0x0,
                0xC8,
                0xE58
            }
    });
    */

    return;
}

void startFetching(MemoryHandler memoryHandler, string[] keyArray)
{
    Task.Run(() =>
    {
        byte[] lastByteArray = new byte[0];

        while (true)
        {
            while (memoryHandler.Scanner.IsScanning) { }

            try
            {
                int keyIndex = 0;
                byte[] byteArray = new byte[0];

                for (int i = 0; i < keyArray.Length; i++)
                {
                    byteArray = memoryHandler.GetByteArray(memoryHandler.Scanner.Locations[keyArray[i]], 256);
                    if (byteArray.Length > 0)
                    {
                        keyIndex = i;
                        byteArray = clearArray(byteArray);
                        break;
                    }
                }

                if (byteArray.Length > 0 && !compareArray(byteArray, lastByteArray))
                {
                    lastByteArray = byteArray;

                    string byteString = Encoding.GetEncoding("utf-8").GetString(byteArray);
                    Console.WriteLine("接收字串(" + keyArray[keyIndex] + "): " + byteString.Replace('\r', ' '));
                    Console.WriteLine("接收位元組(" + keyArray[keyIndex] + "): " + getArrayString(byteArray) + "\n");
                    getArrayString(byteArray);
                    httpPostModule.post(byteString);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }

            try
            {
                Thread.Sleep(100);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }
        }
    });

    return;
}

string getArrayString(byte[] byteArray)
{
    string byteArrayString = "[";

    for (int i = 0; i < byteArray.Length; i++)
    {
        byteArrayString += byteArray[i] + " ";
    }

    byteArrayString += "]";

    return byteArrayString;
}

byte[] clearArray(byte[] byteArray)
{
    List<byte> byteList = new List<byte>();
    int firstZeroIndex = byteArray.ToList().IndexOf(0x00);

    for (int i = 0; i < firstZeroIndex; i++)
    {
        byteList.Add(byteArray[i]);
    }

    return byteList.ToArray();
}

bool compareArray(byte[] a, byte[] b)
{
    if (a.Length != b.Length)
    {
        return false;
    }

    for (int i = 0; i < a.Length; i++)
    {
        if (a[i] != b[i])
        {
            return false;
        }
    }

    return true;
}

class SocketConfig
{
    public string ip = "localhost";
    public int port = 8898;
}

class HttpPostModule
{
    private HttpClient httpClient = new HttpClient();
    private SocketConfig socketConfig = new SocketConfig();

    public HttpPostModule()
    {
        try
        {
            string json = System.IO.File.ReadAllText("server.json");
            SocketConfig? config = JsonConvert.DeserializeObject<SocketConfig>(json);

            if (config != null)
            {
                socketConfig = config;
            }
        }
        catch (Exception exception)
        {
            string serverString = JsonConvert.SerializeObject(new SocketConfig());
            System.IO.File.WriteAllText("server.json", serverString);
            Console.WriteLine(exception.Message);
        }
    }

    public void post(string text)
    {
        string url = "http://" + socketConfig.ip + ":" + socketConfig.port;
        string dataString = JsonConvert.SerializeObject(new
        {
            type = "CUTSCENE",
            code = "003D",
            name = "",
            text = text.Trim()
        });

        Task.Run(() =>
        {
            try
            {
                Thread.Sleep(1000);
                httpClient.PostAsync(url, new StringContent(dataString, Encoding.UTF8, "application/json"));
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }
        }).WaitAsync(TimeSpan.FromMilliseconds(10000));
    }
}