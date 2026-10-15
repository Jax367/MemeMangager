using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace MemeManager {
 public static class Native {
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint message,ref TrayData data);
  [DllImport("user32.dll")] internal static extern IntPtr CreatePopupMenu();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool AppendMenu(IntPtr menu,uint flags,UIntPtr id,string title);
  [DllImport("user32.dll")] internal static extern bool DestroyMenu(IntPtr menu);
  [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr owner,IntPtr rect);
  [DllImport("comdlg32.dll",CharSet=CharSet.Unicode)] static extern bool ChooseColor(ref ColorDialog data);
  [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct TrayData {
   public int Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
   public uint State,StateMask;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
   public uint Timeout;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
   public uint InfoFlags;public Guid Guid;public IntPtr Balloon;
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ColorDialog { public int Size;public IntPtr Owner,Instance;public uint Result;public IntPtr Colors;public uint Flags;public IntPtr Data,Hook,Template; }
  public static string PickColor(IntPtr owner,string initial){var colors=Marshal.AllocHGlobal(64);try{for(int i=0;i<16;i++)Marshal.WriteInt32(colors,i*4,0xffffff);System.Drawing.Color color;try{color=System.Drawing.ColorTranslator.FromHtml(initial);}catch{color=System.Drawing.Color.SlateGray;}var dialog=new ColorDialog{Size=Marshal.SizeOf(typeof(ColorDialog)),Owner=owner,Result=(uint)(color.R|(color.G<<8)|(color.B<<16)),Colors=colors,Flags=3};if(!ChooseColor(ref dialog))return null;return string.Format("#{0:X2}{1:X2}{2:X2}",dialog.Result&255,(dialog.Result>>8)&255,(dialog.Result>>16)&255);}finally{Marshal.FreeHGlobal(colors);}}
  public static string PickFolder(IntPtr owner){var dialog=(IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));try{dialog.SetOptions(0x20|0x40|0x800|0x2000000);dialog.SetTitle("选择表情文件夹（包含子文件夹）");int result=dialog.Show(owner);if(result==unchecked((int)0x800704C7))return null;if(result<0)Marshal.ThrowExceptionForHR(result);IShellItem item;dialog.GetResult(out item);try{IntPtr path;item.GetDisplayName(0x80058000,out path);try{return Marshal.PtrToStringUni(path);}finally{Marshal.FreeCoTaskMem(path);}}finally{Marshal.ReleaseComObject(item);}}finally{Marshal.ReleaseComObject(dialog);}}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string text);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
  public static readonly uint ShowMessage=RegisterWindowMessage("MemeManager.Show.8241D6BA");
  public static readonly uint ImportMessage=RegisterWindowMessage("MemeManager.ImportProcessed.v1");
  public static void Startup(bool enabled,string dataRoot){using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)key.SetValue("MemeManager",StoragePaths.QuoteArgument(System.Reflection.Assembly.GetExecutingAssembly().Location)+" --background --data-dir "+StoragePaths.QuoteArgument(StoragePaths.RequireNonC(dataRoot)));else key.DeleteValue("MemeManager",false);}}
  public static bool IsStartup(){try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return key!=null&&key.GetValue("MemeManager")!=null;}catch(System.Security.SecurityException){return false;}catch(UnauthorizedAccessException){return false;}}
 }
 [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IFileDialog {
  [PreserveSig]int Show(IntPtr owner);void SetFileTypes(uint count,IntPtr types);void SetFileTypeIndex(uint index);void GetFileTypeIndex(out uint index);void Advise(IntPtr sink,out uint cookie);void Unadvise(uint cookie);void SetOptions(uint options);void GetOptions(out uint options);void SetDefaultFolder(IShellItem folder);void SetFolder(IShellItem folder);void GetFolder(out IShellItem folder);void GetCurrentSelection(out IShellItem item);void SetFileName([MarshalAs(UnmanagedType.LPWStr)]string name);void GetFileName(out IntPtr name);void SetTitle([MarshalAs(UnmanagedType.LPWStr)]string title);void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)]string text);void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)]string text);void GetResult(out IShellItem item);void AddPlace(IShellItem item,int placement);void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)]string extension);void Close(int result);void SetClientGuid(ref Guid guid);void ClearClientData();void SetFilter(IntPtr filter);
 }
 [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellItem {
  void BindToHandler(IntPtr context,ref Guid handler,ref Guid id,out IntPtr result);void GetParent(out IShellItem parent);void GetDisplayName(uint type,out IntPtr name);void GetAttributes(uint mask,out uint attributes);void Compare(IShellItem other,uint hints,out int order);
 }
 public sealed class NativeTray:IDisposable {
  public const int Callback=0x8001;Native.TrayData data;readonly Action show,manage,settings,preprocess,exit;readonly uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");System.Drawing.Icon applicationIcon;
  public NativeTray(IntPtr owner,Action onShow,Action onManage,Action onSettings,Action onPreprocess,Action onExit){show=onShow;manage=onManage;settings=onSettings;preprocess=onPreprocess;exit=onExit;try{applicationIcon=System.Drawing.Icon.ExtractAssociatedIcon(typeof(NativeTray).Assembly.Location);}catch{}data=new Native.TrayData{Size=Marshal.SizeOf(typeof(Native.TrayData)),Window=owner,Id=1,Flags=7,Callback=Callback,Icon=applicationIcon!=null?applicationIcon.Handle:System.Drawing.SystemIcons.Application.Handle,Tip="拾趣 · 表情管理",Info="",Title=""};Native.Shell_NotifyIcon(0,ref data);}
  public bool Handle(int message,IntPtr detail){if((uint)message==taskbarCreated){Native.Shell_NotifyIcon(0,ref data);return true;}if(message!=Callback)return false;int notification=detail.ToInt32()&0xffff;if(notification==0x203)show();else if(notification==0x205||notification==0x7B){Native.Point point;Native.GetCursorPos(out point);var menu=Native.CreatePopupMenu();try{Native.AppendMenu(menu,0,new UIntPtr(1),"选择表情");Native.AppendMenu(menu,0,new UIntPtr(2),"表情管理");Native.AppendMenu(menu,0,new UIntPtr(3),"外观与偏好");Native.AppendMenu(menu,0,new UIntPtr(4),"图片预处理");Native.AppendMenu(menu,0x800,UIntPtr.Zero,null);Native.AppendMenu(menu,0,new UIntPtr(5),"退出拾趣");Native.SetForegroundWindow(data.Window);uint choice=Native.TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,data.Window,IntPtr.Zero);Native.PostMessage(data.Window,0,IntPtr.Zero,IntPtr.Zero);if(choice==1)show();else if(choice==2)manage();else if(choice==3)settings();else if(choice==4)preprocess();else if(choice==5)exit();}finally{Native.DestroyMenu(menu);}}return true;}
  public void Dispose(){Native.Shell_NotifyIcon(2,ref data);if(applicationIcon!=null){applicationIcon.Dispose();applicationIcon=null;}}
 }
 public static class Program {
  [STAThread] public static void Main(string[] args){bool first;using(var mutex=new Mutex(true,"Local\\MemeManager.8241D6BA",out first)){if(!first){Native.PostMessage(new IntPtr(0xffff),Array.IndexOf(args,"--import-pending")>=0?Native.ImportMessage:Native.ShowMessage,IntPtr.Zero,IntPtr.Zero);return;}try{StoragePaths.Initialize(args);var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new ManagerWindow();window.Initialize(args);app.Run();}catch(Exception ex){MessageBox.Show(ex.Message,"拾趣启动失败",MessageBoxButton.OK,MessageBoxImage.Error);}}}
 }
}
