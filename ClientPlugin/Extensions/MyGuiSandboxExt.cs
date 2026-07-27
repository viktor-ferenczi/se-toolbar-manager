using System.Text;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Extensions;

public static class MyGuiSandboxExt
{
    public static void Show(StringBuilder text, StringBuilder caption, MyMessageBoxStyleEnum type = MyMessageBoxStyleEnum.Error)
        => MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(type, messageText: text, messageCaption: caption));

    public static void Show(string text, string caption, MyMessageBoxStyleEnum type = MyMessageBoxStyleEnum.Error)
        => Show(new StringBuilder(text), new StringBuilder(caption), type);
}
