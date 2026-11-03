// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DshDesktop {
  internal static class Win {
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int x, y; public Point(int a,int b){x=a;y=b;} }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { public int w,h; public Size(int a,int b){w=a;h=b;} }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int left,top,right,bottom; }
    [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend { public byte op,flags,alpha,format; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int x,y; public uint data,flags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort key,scan; public uint flags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)]public Mouse mouse;[FieldOffset(0)]public Keyboard keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint type;public Union value; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
    internal delegate bool EnumWindow(IntPtr h,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindow callback,IntPtr data);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr h,uint flag);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h,int show);
    [DllImport("user32.dll")] internal static extern uint SendInput(uint n,Input[] a,int size);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int n);
    [DllImport("user32.dll")] internal static extern int GetDpiForSystem();
    [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int mode);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr h,uint affinity);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,ref Point loc,ref Size size,IntPtr src,ref Point from,uint key,ref Blend blend,uint flags);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    internal static void Dpi(){try{SetProcessDpiAwareness(2);}catch{try{SetProcessDPIAware();}catch{}}}
    internal static string Title(IntPtr h){var b=new StringBuilder(1024);GetWindowText(h,b,b.Capacity);return b.ToString();}
    internal static void Send(Input i){if(SendInput(1,new[]{i},Marshal.SizeOf(typeof(Input)))!=1)throw new Exception("Input rejected by Windows (permission/UIPI)");}
    internal static void Key(ushort k,bool up){var i=new Input();i.type=1;i.value.keyboard.key=k;i.value.keyboard.flags=up?2u:0u;Send(i);}
    internal static void Unicode(char c,bool up){var i=new Input();i.type=1;i.value.keyboard.scan=c;i.value.keyboard.flags=4u|(up?2u:0u);Send(i);}
    internal static void MouseButton(bool up,string button="left"){var i=new Input();i.value.mouse.flags=button=="right"?(up?16u:8u):button=="middle"?(up?64u:32u):(up?4u:2u);Send(i);}
    internal static void Wheel(int amount){var i=new Input();i.value.mouse.flags=0x800;i.value.mouse.data=unchecked((uint)amount);Send(i);}
    internal static void Move(int x,int y){
      int vx=GetSystemMetrics(76),vy=GetSystemMetrics(77),w=GetSystemMetrics(78),h=GetSystemMetrics(79);
      if(x<vx||y<vy||x>=vx+w||y>=vy+h)throw new Exception("Point outside virtual desktop");
      var i=new Input();i.value.mouse.x=(int)Math.Round((x-vx)*65535.0/Math.Max(1,w-1));
      i.value.mouse.y=(int)Math.Round((y-vy)*65535.0/Math.Max(1,h-1));i.value.mouse.flags=0xC001;Send(i);
    }
  }

  internal sealed class Surface:Form {
    internal bool ExcludedFromCapture;
    internal Surface(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x080800A0;return p;}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ExcludedFromCapture=Win.SetWindowDisplayAffinity(Handle,0x11);}
    protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
    internal void Image(Bitmap bitmap,int x,int y){
      IntPtr screen=Win.GetDC(IntPtr.Zero),memory=Win.CreateCompatibleDC(screen),image=IntPtr.Zero,previous=IntPtr.Zero;
      try{
        image=bitmap.GetHbitmap(Color.FromArgb(0));previous=Win.SelectObject(memory,image);
        var location=new Win.Point(x,y);var size=new Win.Size(bitmap.Width,bitmap.Height);var from=new Win.Point(0,0);
        var blend=new Win.Blend{op=0,flags=0,alpha=255,format=1};
        if(!Win.UpdateLayeredWindow(Handle,screen,ref location,ref size,memory,ref from,0,ref blend,2))throw new System.ComponentModel.Win32Exception();
      }finally{if(previous!=IntPtr.Zero)Win.SelectObject(memory,previous);if(image!=IntPtr.Zero)Win.DeleteObject(image);Win.DeleteDC(memory);Win.ReleaseDC(IntPtr.Zero,screen);}
    }
    internal void Position(int x,int y){Win.SetWindowPos(Handle,new IntPtr(-1),x,y,0,0,0x11);}
  }
}
