using Newtonsoft.Json;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Enums;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

HttpPostModule httpPostModule = new HttpPostModule();

string configPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

int _previousArrayIndex = 0;
int _previousOffset = 0;

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
        memoryHandler.Scanner.Locations.Clear();

        List<Signature> signatures = new List<Signature>();
        addSignature(signatures);

        memoryHandler.Scanner.LoadOffsets(signatures.ToArray());
        getChatLog(memoryHandler);
        getDialog(memoryHandler);
        getCutscene(memoryHandler, new string[] { "CUTSCENE_TEXT" }, 0, 256);

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
        Key = "CHATLOG",
        ASMSignature = true,
        PointerPath = new List<long>
        {
            0,
            0,
            64,
            908
        },
        Value = "488941104488492C4C8949244C89491C4584C07412488B4218488905********48890D",
    });

    signatures.Add(new Signature
    {
        Key = "PANEL_NAME",
        ASMSignature = true,
        PointerPath = new List<long>
        {
            0,
            0,
            32,
            14504,
            544,
            218
        },
        Value = "488B4708498B5D00498B7728488B3D",
    });

    signatures.Add(new Signature
    {
        Key = "PANEL_TEXT",
        ASMSignature = true,
        PointerPath = new List<long>
        {
            0,
            0,
            32,
            14504,
            552,
            184
        },
        Value = "488B4708498B5D00498B7728488B3D",
    });

    signatures.Add(new Signature
    {
        Key = "CUTSCENE_TEXT",
        PointerPath = new List<long>
        {
            0x020D0CC0,
            0x68,
            0x250,
            0x0
        }
    });

    return;
}

void getChatLog(MemoryHandler memoryHandler)
{
    Task.Run(() =>
    {
        string lastChatLotText = "";

        while (true)
        {
            while (memoryHandler.Scanner.IsScanning) { }

            ChatLogResult readResult = memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);

            List<ChatLogItem> chatLogEntries = readResult.ChatLogItems.ToList();

            _previousArrayIndex = readResult.PreviousArrayIndex;
            _previousOffset = readResult.PreviousOffset;


            if ((chatLogEntries.Count > 0 && chatLogEntries[0].Code != "003D" && chatLogEntries[0].Message != lastChatLotText))
            {
                lastChatLotText = chatLogEntries[0].Message;

                string logMessage = chatLogEntries[0].Message;
                string logName = logMessage.Split(':')[0];
                string logText = logMessage.Replace(logName, "");
                httpPostModule.post("CHAT_LOG", chatLogEntries[0].Code, logName, new TextCleaner().clearText(logText));

                Console.WriteLine("對話紀錄字串: (" + chatLogEntries[0].Code + ")" + chatLogEntries[0].Message.Replace('\r', ' ') + "\n");
            }

            delay();
        }
    });
}

void getDialog(MemoryHandler memoryHandler)
{
    Task.Run(() =>
    {
        string lastPanelText = "";

        while (true)
        {
            while (memoryHandler.Scanner.IsScanning) { }
            string[] result = GetDialogPanel(memoryHandler);

            if (result.Length > 0 && result[1] != lastPanelText)
            {
                lastPanelText = result[1];
                httpPostModule.post("DIALOG", "003D", result[0], result[1]);
                Console.WriteLine("對話框字串: " + result[0] + ": " + result[1].Replace('\r', ' ') + "\n");
            }

            delay();
        }


    });
}

string[] GetDialogPanel(MemoryHandler memoryHandler)
{
    string[] result = new string[] { };

    var dialogPanelNamePointer = (IntPtr)memoryHandler.Scanner.Locations["PANEL_NAME"];
    var dialogPanelNameLengthPointer = IntPtr.Subtract(dialogPanelNamePointer, 18);

    var dialogPanelTextPointer = (IntPtr)memoryHandler.Scanner.Locations["PANEL_TEXT"];
    var dialogPanelText = new IntPtr(memoryHandler.GetInt64(dialogPanelTextPointer));

    var dialogPanelTextLegthPointer = IntPtr.Add(dialogPanelTextPointer, 16);

    int nameLength = (int)memoryHandler.GetInt64(dialogPanelNameLengthPointer);
    int textLength = (int)memoryHandler.GetInt64(dialogPanelTextLegthPointer);

    if (textLength > 1 && nameLength > 1)
    {
        if (textLength > 512)
            textLength = 512;

        if (nameLength > 128)
            nameLength = 128;

        byte[] npcNameBytes = memoryHandler.GetByteArray(dialogPanelNamePointer, nameLength);
        byte[] textBytes = memoryHandler.GetByteArray(dialogPanelText, textLength);

        nameLength = GetRealTextLength(ref npcNameBytes);
        textLength = GetRealTextLength(ref textBytes);

        //Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        //byte[] unixTimestampBytes = BitConverter.GetBytes(unixTimestamp).ToArray();

        result = new string[] { byteToString(npcNameBytes), byteToString(textBytes) };
    }

    return result;
}

int GetRealTextLength(ref byte[] byteArray)
{
    int textEnd = 0;

    for (int i = 0; i < byteArray.Length; i++)
    {
        if (byteArray[i] == 0)
        {
            textEnd = i + 1;
            break;
        }
    }

    if (textEnd != 0 && textEnd <= byteArray.Length)
    {
        byte[] newArr = new byte[textEnd];
        Array.Copy(byteArray, newArr, newArr.Length);
        byteArray = newArr;
    }
    else
    {
        byteArray = new byte[0];
    }

    return textEnd;
}

void getCutscene(MemoryHandler memoryHandler, string[] keyArray, int startIndex, int byteLength)
{
    Task.Run(() =>
    {
        byte[] lastByteArray = new byte[0];

        while (true)
        {
            while (memoryHandler.Scanner.IsScanning) { }

            try
            {
                //int keyIndex = 0;
                byte[] byteArray = new byte[0];

                for (int i = 0; i < keyArray.Length; i++)
                {
                    byteArray = memoryHandler.GetByteArray(memoryHandler.Scanner.Locations[keyArray[i]], byteLength);
                    if (byteArray.Length > 0)
                    {
                        //keyIndex = i;
                        byteArray = clearArray(byteArray, startIndex);
                        break;
                    }
                }

                if (byteArray.Length > 0 && !compareArray(byteArray, lastByteArray))
                {
                    lastByteArray = byteArray;

                    string byteString = byteToString(byteArray);
                    httpPostModule.post("CUTSCENE", "003D", "", byteString, 1000);
                    Console.WriteLine("過場字串: " + byteString.Replace('\r', ' ') + "\n過場位元組: " + getArrayString(byteArray) + "\n");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }

            delay();
        }
    });

    return;
}

string byteToString(byte[] byteArray)
{
    string byteString = Encoding.GetEncoding("utf-8").GetString(byteArray);
    byteString = new TextCleaner().clearText(byteString);
    return byteString;
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

byte[] clearArray(byte[] byteArray, int startIndex)
{
    List<byte> byteList = new List<byte>();
    int firstZeroIndex = byteArray.ToList().IndexOf(0x00, startIndex);

    for (int i = startIndex; i < firstZeroIndex; i++)
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

void delay(int delayTIme = 100)
{
    try
    {
        Thread.Sleep(delayTIme);
    }
    catch (Exception exception)
    {
        Console.WriteLine(exception.Message);
    }
}

class TextCleaner
{
    private static readonly Regex ArrowRegex = new Regex(@"", RegexOptions.Compiled);
    private static readonly Regex HQRegex = new Regex(@"", RegexOptions.Compiled);
    private static readonly Regex NoPrintingCharactersRegex = new Regex(@"[\x00-\x1F]+", RegexOptions.Compiled);
    private static readonly Regex SpecialPurposeUnicodeRegex = new Regex(@"[\uE000-\uF8FF]", RegexOptions.Compiled);
    private static readonly Regex SpecialReplacementRegex = new Regex(@"[�]", RegexOptions.Compiled);

    public string clearText(string text)
    {
        // replace right arrow in chat (parsing)
        text = ArrowRegex.Replace(text, "⇒");
        // replace HQ symbol
        text = HQRegex.Replace(text, "[HQ]");
        // replace all Extended special purpose unicode with empty string
        text = SpecialPurposeUnicodeRegex.Replace(text, string.Empty);
        // cleanup special replacement character bytes: 239 191 189
        text = SpecialReplacementRegex.Replace(text, string.Empty);
        // remove characters 0-31
        text = NoPrintingCharactersRegex.Replace(text, string.Empty);
        // remove 「H%I& and IH」
        text = text.Replace("「H%I&", "「");
        text = text.Replace("IH」", "」");

        return text;
    }
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

    public void post(string type, string code, string name, string text, int sleepTime = 0)
    {
        string url = "http://" + socketConfig.ip + ":" + socketConfig.port;
        string dataString = JsonConvert.SerializeObject(new
        {
            type = type,
            code = code.Trim(),
            name = name.Trim(),
            text = text.Trim()
        });

        Task.Run(() =>
        {
            try
            {
                Thread.Sleep(sleepTime);
                httpClient.PostAsync(url, new StringContent(dataString, Encoding.UTF8, "application/json"));
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }
        }).WaitAsync(TimeSpan.FromMilliseconds(10000));
    }
}