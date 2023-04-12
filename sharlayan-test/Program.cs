#region Using
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Enums;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
#endregion

#region Global Variable
bool isScanning = false;

int _previousArrayIndex = 0;
int _previousOffset = 0;

string lastSystemMessage = "";
string lastChatLogString = "";
string lastDialogString = "";
string lastCutsceneString = "";

List<string> dialogHistory = new List<string>();
#endregion

#region Main Process
MainProcess();

void MainProcess()
{
    try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

    while (true)
    {
        try
        {
            MemoryHandler memoryHandler = GetGameProcess();
            isScanning = true;
            WriteSystemMessage("讀取字幕中，請勿關閉本視窗");
            while (isScanning)
            {
                RunScanner(memoryHandler);
                TaskDelay();
            }
        }
        catch (Exception exception)
        {
            WriteSystemMessage(exception.Message);
        }
        TaskDelay(1000);
    }
}

MemoryHandler GetGameProcess()
{
    try
    {
        SharlayanConfiguration configuration = new SharlayanConfiguration
        {
            ProcessModel = new ProcessModel
            {
                Process = Process.GetProcessesByName("ffxiv_dx11").FirstOrDefault()
            },
            GameLanguage = GameLanguage.English,
            GameRegion = GameRegion.Global,
            PatchVersion = "latest",
            UseLocalCache = true,
            ScanAllRegions = false
        };

        MemoryHandler memoryHandler = SharlayanMemoryManager.Instance.AddHandler(configuration);

        List<Signature> signatures = new List<Signature>();
        AddSignature(signatures);
        memoryHandler.Scanner.LoadOffsets(signatures.ToArray());

        return memoryHandler;
    }
    catch (Exception exception)
    {
        throw new Exception("等待FFXIV啟動...(" + exception.Message + ")");
    }
}

void AddSignature(List<Signature> signatures)
{
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

    signatures.Add(new Signature
    {
        Key = "CUTSCENE_DETECTOR",
        ASMSignature = true,
        PointerPath = new List<long>
        {
            0,
            0
        },
        Value = "488B0D********881D",
    });

    return;
}
#endregion

#region ChatLogScanner
void ChatLogScanner(MemoryHandler memoryHandler)
{
    try
    {
        ChatLogResult readResult = memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);
        List<ChatLogItem> chatLogEntries = readResult.ChatLogItems.ToList();

        _previousArrayIndex = readResult.PreviousArrayIndex;
        _previousOffset = readResult.PreviousOffset;

        if (chatLogEntries.Count > 0)
        {
            for (int i = 0; i < chatLogEntries.Count; i++)
            {
                ChatLogItem chatLogItem = chatLogEntries[i];

                if (chatLogItem.Message != lastChatLogString)
                {
                    lastChatLogString = chatLogItem.Message;

                    string chatLogText = chatLogItem.Message;
                    string[] splitLogmessage = chatLogText.Split(':');
                    string logName = splitLogmessage.Length > 1 ? splitLogmessage[0] : "";
                    string logText = logName != "" ? chatLogText.Replace(logName + ":", "") : chatLogText;
                    Console.WriteLine("對話紀錄字串: (" + chatLogItem.Code + ")" + chatLogItem.Message.Replace('\r', ' '));

                    if (chatLogItem.Code != "003D" || checkHistory(logText))
                    {
                        new HttpModule().Post("CHAT_LOG", chatLogItem.Code, logName, logText);
                    }
                }
            }
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine("ChatLogScanner: " + exception.Message);
        isScanning = false;
    }

    return;
}

void addHistory(string text)
{
    text = new TextCleaner().ClearText(text.Replace("\r", ""));
    dialogHistory.Add(text);
    if (dialogHistory.Count > 20)
    {
        int newCount = dialogHistory.Count / 2;
        dialogHistory.RemoveRange(0, newCount);
    }
}

bool checkHistory(string text)
{
    text = new TextCleaner().ClearText(text.Replace("\r", ""));
    List<string> history = dialogHistory.ToList();

    if (history.Count > 0)
    {
        int lastIndex = history.LastIndexOf(text);
        return lastIndex < history.Count - 2;
    }
    else
    {
        return true;
    }
}
#endregion

#region DialogScanner
void DialogScanner(MemoryHandler memoryHandler)
{
    try
    {
        string[] result = GetDialogPanel(memoryHandler);

        if (result.Length > 0 && result[1] != lastDialogString)
        {
            lastDialogString = result[1];
            addHistory(result[1]);
            Console.WriteLine("對話框字串: " + result[0] + ": " + result[1].Replace('\r', ' '));
            new HttpModule().Post("DIALOG", "003D", result[0], result[1]);
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine("DialogScanner: " + exception.Message);
        isScanning = false;
    }

    return;
}

string[] GetDialogPanel(MemoryHandler memoryHandler)
{
    string[] result = new string[] { };

    var dialogPanelNamePointer = (IntPtr)memoryHandler.Scanner.Locations["PANEL_NAME"];
    var dialogPanelNameLengthPointer = IntPtr.Subtract(dialogPanelNamePointer, 18);

    var dialogPanelTextPointer = (IntPtr)memoryHandler.Scanner.Locations["PANEL_TEXT"];
    var dialogPanelTextPointer2 = new IntPtr((long)memoryHandler.GetUInt64(dialogPanelTextPointer));

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
        byte[] textBytes = memoryHandler.GetByteArray(dialogPanelTextPointer2, textLength);

        nameLength = GetRealTextLength(ref npcNameBytes);
        textLength = GetRealTextLength(ref textBytes);

        //Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        //byte[] unixTimestampBytes = BitConverter.GetBytes(unixTimestamp).ToArray();

        result = new string[] { ByteToString(npcNameBytes), ByteToString(textBytes) };
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
#endregion

#region CutsceneScanner
void CutsceneScanner(MemoryHandler memoryHandler)
{
    try
    {
        var cutsceneDetector = (IntPtr)memoryHandler.Scanner.Locations["CUTSCENE_DETECTOR"];
        int isCutscene = (int)memoryHandler.GetInt64(cutsceneDetector);

        if (isCutscene == 1) return;

        byte[] byteArray = new byte[0];
        string byteString = "";

        byteArray = memoryHandler.GetByteArray(memoryHandler.Scanner.Locations["CUTSCENE_TEXT"], 256);

        if (byteArray.Length > 0)
        {
            byteArray = ClearArray(byteArray);
            byteString = ByteToString(byteArray);

            if (byteString != lastCutsceneString)
            {
                lastCutsceneString = byteString;
                Console.WriteLine("過場字串: " + byteString.Replace('\r', ' ') + "\n過場位元組: " + ArrayToString(byteArray));
                new HttpModule().Post("CUTSCENE", "0044", "", byteString, 1000);
            }
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine("CutsceneScanner: " + exception.Message);
        isScanning = false;
    }

    return;
}
#endregion

#region Byte Functions
/*
bool CompareArray(byte[] byteArray1, byte[] byteArray2)
{
    if (byteArray1.Length != byteArray2.Length) { return false; }

    for (int i = 0; i < byteArray1.Length; i++)
    {
        if (byteArray1[i] != byteArray2[i]) { return false; }
    }

    return true;
}
*/

string ByteToString(byte[] byteArray)
{
    return Encoding.UTF8.GetString(byteArray);
}

string ArrayToString(byte[] byteArray)
{
    string arrayString = "[";

    for (int i = 0; i < byteArray.Length; i++)
    {
        arrayString += byteArray[i] + " ";
    }

    arrayString += "]";

    return arrayString;
}

byte[] ClearArray(byte[] byteArray, int startIndex = 0)
{
    List<byte> byteList = new List<byte>();
    int firstZeroIndex = byteArray.ToList().IndexOf(0x00, startIndex);

    for (int i = startIndex; i < firstZeroIndex; i++)
    {
        byteList.Add(byteArray[i]);
    }

    return byteList.ToArray();
}
#endregion

#region System Functions
void RunScanner(MemoryHandler memoryHandler)
{
    if (!memoryHandler.Scanner.IsScanning)
    {
        ChatLogScanner(memoryHandler);
        DialogScanner(memoryHandler);
        CutsceneScanner(memoryHandler);
    }
}

void TaskDelay(int delayTIme = 20)
{
    try
    {
        Task.Delay(delayTIme).Wait();
    }
    catch (Exception exception)
    {
        WriteSystemMessage("TaskDelay: " + exception.Message);
    }
}

void WriteSystemMessage(string message)
{
    if (message != lastSystemMessage)
    {
        lastSystemMessage = message;
        Console.WriteLine(lastSystemMessage + "\n");
    }
}
#endregion

#region Class Definition
class TextCleaner
{
    private static readonly Regex ArrowRegex = new Regex(@"", RegexOptions.Compiled);
    private static readonly Regex HQRegex = new Regex(@"", RegexOptions.Compiled);
    private static readonly Regex NoPrintingCharactersRegex = new Regex(@"[\x00-\x0C\x0E-\x1F]+", RegexOptions.Compiled);
    private static readonly Regex SpecialPurposeUnicodeRegex = new Regex(@"[\uE000-\uF8FF]", RegexOptions.Compiled);
    private static readonly Regex SpecialReplacementRegex = new Regex(@"[�]", RegexOptions.Compiled);
    private static readonly Regex ItemRegex = new Regex(@"H%I&(.+?)IH", RegexOptions.Compiled);

    public string ClearText(string text)
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

        // remove H%I& and IH
        text = ItemRegex.Replace(text, "$1");

        return text.Trim();
    }
}

class HttpModule
{
    private static SocketConfig? Config = null;
    private HttpClient Client = new HttpClient();
    private string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Tataru Helper Node\setting\config.json");

    public HttpModule()
    {
        if (Config == null)
        {
            SetConfig();
        }
    }

    private void SetConfig()
    {
        try
        {
            string json = System.IO.File.ReadAllText(ConfigPath);
            JObject? data = JObject.Parse(json);
            SocketConfig? config = data?["server"]?.ToObject<SocketConfig>();

            if (config != null)
            {
                Config = config;
            }
            else
            {
                Config = new SocketConfig();
            }
        }
        catch (Exception)
        {
            Config = new SocketConfig();
        }
    }

    public async void Post(string type, string code, string name, string text, int sleepTime = 0, bool isRetry = false)
    {
        string url = "http://" + Config?.IP + ":" + Config?.Port;
        string dataString = JsonConvert.SerializeObject(new
        {
            type,
            code,
            name = new TextCleaner().ClearText(name),
            text = new TextCleaner().ClearText(text)
        });

        try
        {
            await Task.Delay(sleepTime);
            await Client.PostAsync(url, new StringContent(dataString, Encoding.UTF8, "application/json"));
        }
        catch (Exception)
        {
            SetConfig();
            if (!isRetry) { Post(type, code, name, text, sleepTime, true); }
        }

        return;
    }

    private class SocketConfig
    {
        public string IP = "localhost";
        public int Port = 8898;
    }
}
#endregion