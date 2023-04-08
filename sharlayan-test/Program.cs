#region Using
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
#endregion

#region Global Variable
bool isRunning = false;

int _previousArrayIndex = 0;
int _previousOffset = 0;

string lastSystemMessage = "";
string lastChatLogText = "";
string lastDialogText = "";
string lastCutsceneText = "";
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
            MemoryHandler? memoryHandler = GetGameProcess();

            if (memoryHandler != null)
            {
                isRunning = true;

                WriteSystemMessage("讀取字幕中，請勿關閉本視窗\n");
                ChatLogScanner(memoryHandler);
                DialogScanner(memoryHandler);
                CutsceneScanner(memoryHandler, new string[] { "CUTSCENE_TEXT" }, 0, 256);

                while (isRunning) { SystemDelay(1000); }
            }
        }
        catch (Exception exception)
        {
            WriteSystemMessage("MainProcess: " + exception.Message);
            isRunning = false;
        }

        SystemDelay(1000);
    }
}

MemoryHandler? GetGameProcess()
{
    try
    {
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
        AddSignature(signatures);
        memoryHandler.Scanner.LoadOffsets(signatures.ToArray());

        return memoryHandler;
    }
    catch (Exception exception)
    {
        WriteSystemMessage("等待FFXIV啟動...(" + exception.Message + ")\n");
        isRunning = false;
        return null;
    }
}

void AddSignature(List<Signature> signatures)
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
#endregion

#region ChatLogScanner
void ChatLogScanner(MemoryHandler memoryHandler)
{
    Task.Run(() =>
    {
        while (isRunning)
        {
            while (memoryHandler.Scanner.IsScanning) { SystemDelay(); }

            try
            {
                ChatLogResult readResult = memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);

                List<ChatLogItem> chatLogEntries = readResult.ChatLogItems.ToList();

                _previousArrayIndex = readResult.PreviousArrayIndex;
                _previousOffset = readResult.PreviousOffset;

                if (chatLogEntries.Count > 0 && chatLogEntries[0].Code != "003D" && chatLogEntries[0].Message != lastChatLogText)
                {
                    lastChatLogText = chatLogEntries[0].Message;

                    string logMessage = chatLogEntries[0].Message;
                    string[] splitLogmessage = logMessage.Split(':');
                    string logName = splitLogmessage.Length > 1 ? splitLogmessage[0] : "";
                    string logText = logName != "" ? logMessage.Replace(logName + ":", "") : logMessage;
                    HttpModule httpModule = new HttpModule();
                    httpModule.Post("CHAT_LOG", chatLogEntries[0].Code, logName, logText);

                    WriteSystemMessage("對話紀錄字串: (" + chatLogEntries[0].Code + ")" + chatLogEntries[0].Message.Replace('\r', ' ') + "\n");
                }
            }
            catch (Exception exception)
            {
                WriteSystemMessage("ChatLogScanner: " + exception.Message);
                isRunning = false;
            }

            SystemDelay();
        }

        return;
    });

    return;
}
#endregion

#region DialogScanner
void DialogScanner(MemoryHandler memoryHandler)
{
    Task.Run(() =>
    {
        while (isRunning)
        {
            while (memoryHandler.Scanner.IsScanning) { SystemDelay(); }

            try
            {
                string[] result = GetDialogPanel(memoryHandler);

                if (result.Length > 0 && result[1] != lastDialogText)
                {
                    lastDialogText = result[1];
                    HttpModule httpModule = new HttpModule();
                    httpModule.Post("DIALOG", "003D", result[0], result[1]);
                    WriteSystemMessage("對話框字串: " + result[0] + ": " + result[1].Replace('\r', ' ') + "\n");
                }
            }
            catch (Exception exception)
            {
                WriteSystemMessage("DialogScanner: " + exception.Message);
                isRunning = false;
            }

            SystemDelay();
        }

        return;
    });

    return;
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
void CutsceneScanner(MemoryHandler memoryHandler, string[] keyArray, int startIndex, int byteLength)
{
    Task.Run(() =>
    {
        string byteString = "";

        while (isRunning)
        {
            while (memoryHandler.Scanner.IsScanning) { SystemDelay(); }

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
                        byteArray = ClearArray(byteArray, startIndex);
                        byteString = ByteToString(byteArray);
                        break;
                    }
                }

                if (byteArray.Length > 0 && byteString != lastCutsceneText)
                {
                    lastCutsceneText = byteString;
                    HttpModule httpModule = new HttpModule();
                    httpModule.Post("CUTSCENE", "0044", "", lastCutsceneText, 1000);
                    WriteSystemMessage("過場字串: " + lastCutsceneText.Replace('\r', ' ') + "\n過場位元組: " + ArrayToString(byteArray) + "\n");
                }
            }
            catch (Exception exception)
            {
                WriteSystemMessage("CutsceneScanner: " + exception.Message);
                isRunning = false;
            }

            SystemDelay();
        }

        return;
    });

    return;
}
#endregion

#region Byte Functions
string ByteToString(byte[] byteArray)
{
    return Encoding.GetEncoding("utf-8").GetString(byteArray);
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
void SystemDelay(int delayTIme = 10)
{
    try
    {
        Thread.Sleep(delayTIme);
    }
    catch (Exception exception)
    {
        WriteSystemMessage("SystemDelay: " + exception.Message);
    }
}

void WriteSystemMessage(string message)
{
    if (message != lastSystemMessage)
    {
        lastSystemMessage = message;
        Console.WriteLine(lastSystemMessage);
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

        return text;
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
            else {
                Config = new SocketConfig();
            }
        }
        catch (Exception)
        {
            Config = new SocketConfig();
        }
    }

    public void Post(string type, string code, string name, string text, int sleepTime = 0, bool isRetry = false)
    {
        string url = "http://" + Config.IP + ":" + Config.Port;
        string dataString = JsonConvert.SerializeObject(new
        {
            type,
            code,
            name = new TextCleaner().ClearText(name.Trim()),
            text = new TextCleaner().ClearText(text.Trim())
        });

        Task.Run(async () =>
        {
            try
            {
                Thread.Sleep(sleepTime);
                await Client.PostAsync(url, new StringContent(dataString, Encoding.UTF8, "application/json"));
            }
            catch (Exception)
            {
                SetConfig();
                if (!isRetry) { Post(type, code, name, text, sleepTime, true); }
            }

            return;
        });

        return;
    }

    private class SocketConfig
    {
        public string IP = "localhost";
        public int Port = 8898;
    }
}
#endregion