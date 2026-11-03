// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DshDesktop {
  internal sealed class FeedbackState {
    public string mode="active",label="正在操作",cancelFile,leaseFile;
    public long expiresAt;
    public int ownerPid;
    public bool pinPointer;
    public int pointerX,pointerY;
  }
  internal static class Art {
    internal static Color Accent=Color.FromArgb(112,125,255);
    internal static GraphicsPath Round(RectangleF r,float radius){
      var p=new GraphicsPath();float d=radius*2;
      p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    internal static Bitmap Edge(int w,int h,int direction){
      var b=new Bitmap(w,h,PixelFormat.Format32bppPArgb);
      using(var g=Graphics.FromImage(b)){
        int thickness=(direction<2?w:h);
        for(int n=0;n<thickness;n++){
          double distance=(double)n/thickness;int alpha=(int)(115*Math.Pow(1-distance,2.5));
          using(var pen=new Pen(Color.FromArgb(alpha,112,125,255),1)){
            if(direction==0)g.DrawLine(pen,n,0,n,h);
            if(direction==1)g.DrawLine(pen,w-1-n,0,w-1-n,h);
            if(direction==2)g.DrawLine(pen,0,n,w,n);
            if(direction==3)g.DrawLine(pen,0,h-1-n,w,h-1-n);
          }
        }
      }return b;
    }
    internal static Bitmap Pointer(float scale=1){
      var b=new Bitmap((int)(100*scale),(int)(108*scale),PixelFormat.Format32bppPArgb);
      using(var g=Graphics.FromImage(b)){
        g.ScaleTransform(scale,scale);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var points=new[]{new PointF(10,8),new PointF(10,64),new PointF(25,50),new PointF(39,77),new PointF(51,71),new PointF(36,45),new PointF(57,45)};
        using(var path=new GraphicsPath()){
          path.AddPolygon(points);
          using(var shadow=new Pen(Color.FromArgb(45,0,0,0),11)){shadow.LineJoin=LineJoin.Round;g.DrawPath(shadow,path);}
          using(var outline=new Pen(Color.White,4)){outline.LineJoin=LineJoin.Round;g.DrawPath(outline,path);}
          using(var brush=new SolidBrush(Color.FromArgb(255,16,19,26)))g.FillPath(brush,path);
        }
        using(var brush=new SolidBrush(Accent))g.FillEllipse(brush,57,65,27,27);
        using(var font=new Font("Segoe UI",8,FontStyle.Bold))using(var brush=new SolidBrush(Color.White))g.DrawString("AI",font,brush,62,71);
      }return b;
    }
    internal static Bitmap Badge(string label,bool preview,float scale=1,string mode="active"){
      var b=new Bitmap((int)(364*scale),(int)(88*scale),PixelFormat.Format32bppPArgb);
      using(var g=Graphics.FromImage(b)){
        g.ScaleTransform(scale,scale);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Round(new RectangleF(3,3,358,81),18)){
          using(var brush=new SolidBrush(Color.FromArgb(246,17,20,29)))g.FillPath(brush,p);
          using(var pen=new Pen(Color.FromArgb(100,112,125,255),1))g.DrawPath(pen,p);
        }
        using(var brush=new SolidBrush(Accent))g.FillEllipse(brush,21,21,9,9);
        string heading=preview?"DSH · 视觉预览":mode=="preparing"?"DSH · 准备操作":mode=="sent"?"DSH · 已发送操作":mode=="verified"?"DSH · 已核对结果":mode=="error"?"DSH · 操作已停止":"DSH · 正在操作";
        using(var title=new Font("Microsoft YaHei UI",11,FontStyle.Bold))using(var white=new SolidBrush(Color.White))g.DrawString(heading,title,white,41,13);
        using(var font=new Font("Microsoft YaHei UI",8.5f))using(var muted=new SolidBrush(Color.FromArgb(185,194,211))){
          g.DrawString(label,font,muted,new RectangleF(20,39,324,18));
          g.DrawString(preview?"Ctrl + Alt + Esc  退出预览":"Ctrl + Alt + Esc  停止本次操作",font,muted,20,59);
        }
      }return b;
    }
    internal static void Preview(string path){
      using(var b=new Bitmap(1440,900))using(var g=Graphics.FromImage(b)){
        g.Clear(Color.FromArgb(29,34,44));g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var fill=new SolidBrush(Color.FromArgb(39,45,57)))g.FillRectangle(fill,0,0,1440,52);
        using(var f=new Font("Microsoft YaHei UI",12))using(var br=new SolidBrush(Color.FromArgb(187,196,212)))g.DrawString("你的桌面与应用",f,br,36,16);
        using(var p=Round(new RectangleF(295,222,850,456),16))using(var br=new SolidBrush(Color.FromArgb(243,245,249)))g.FillPath(br,p);
        using(var f=new Font("Microsoft YaHei UI",22,FontStyle.Bold))using(var br=new SolidBrush(Color.FromArgb(40,48,65)))g.DrawString("操作位置，一眼就能看见",f,br,352,290);
        using(var f=new Font("Microsoft YaHei UI",12))using(var br=new SolidBrush(Color.FromArgb(100,110,130)))g.DrawString("大指针指向目标；屏幕边缘标识自动化正在进行。",f,br,354,351);
        using(var p=Round(new RectangleF(354,430,280,58),11))using(var br=new SolidBrush(Accent))g.FillPath(br,p);
        using(var f=new Font("Microsoft YaHei UI",12,FontStyle.Bold))using(var br=new SolidBrush(Color.White))g.DrawString("目标控件",f,br,446,447);
        using(var left=Edge(34,900,0))g.DrawImage(left,0,0);
        using(var right=Edge(34,900,1))g.DrawImage(right,1406,0);
        using(var top=Edge(1440,34,2))g.DrawImage(top,0,0);
        using(var bottom=Edge(1440,34,3))g.DrawImage(bottom,0,866);
        using(var badge=Badge("正在定位输入框",false))g.DrawImage(badge,538,24);
        using(var pointer=Pointer())g.DrawImage(pointer,602,457);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));b.Save(path,ImageFormat.Png);
      }
    }
  }
  internal sealed class FeedbackContext:ApplicationContext {
    readonly string statePath,readyPath;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly List<Surface> surfaces=new List<Surface>();
    readonly Surface cursor=new Surface(),badge=new Surface();
    readonly Timer timer=new Timer();
    readonly Stopwatch age=Stopwatch.StartNew();
    readonly Bitmap pointer;
    readonly float scale;
    string lastLabel="",lastMode="";FeedbackState state;
    readonly HotkeyWindow hotkey;
    internal FeedbackContext(string stateFile,string readyFile){
      statePath=stateFile;readyPath=readyFile;
      try{scale=Math.Max(1,Win.GetDpiForSystem()/96f);}catch{scale=1;}
      pointer=Art.Pointer(scale);
      foreach(var screen in Screen.AllScreens){
        var r=screen.Bounds;int t=(int)(32*scale);
        AddEdge(Art.Edge(t,r.Height,0),r.Left,r.Top);AddEdge(Art.Edge(t,r.Height,1),r.Right-t,r.Top);
        AddEdge(Art.Edge(r.Width,t,2),r.Left,r.Top);AddEdge(Art.Edge(r.Width,t,3),r.Left,r.Bottom-t);
      }
      cursor.Image(pointer,0,0);cursor.Show();Win.ShowWindow(cursor.Handle,8);badge.Show();Win.ShowWindow(badge.Handle,8);
      hotkey=new HotkeyWindow(Cancel);
      timer.Interval=32;timer.Tick+=Tick;timer.Start();
      Tick(null,EventArgs.Empty);
      Win.Rect badgeBounds;Win.GetWindowRect(badge.Handle,out badgeBounds);
      File.WriteAllText(readyPath,json.Serialize(new{pid=Process.GetCurrentProcess().Id,windows=surfaces.Count+2,
        clickThrough=true,noActivate=true,captureExcluded=cursor.ExcludedFromCapture&&badge.ExcludedFromCapture,hotkeyRegistered=hotkey.Registered,
        badgePoint=new{x=(badgeBounds.left+badgeBounds.right)/2,y=(badgeBounds.top+badgeBounds.bottom)/2}}));
    }
    void AddEdge(Bitmap image,int x,int y){var s=new Surface();s.Image(image,x,y);s.Show();Win.ShowWindow(s.Handle,8);surfaces.Add(s);image.Dispose();}
    void Tick(object sender,EventArgs e){
      try{state=json.Deserialize<FeedbackState>(File.ReadAllText(statePath));}
      catch{if(age.ElapsedMilliseconds>3000)ExitThread();return;}
      if(state==null||state.mode=="stop"||DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()>state.expiresAt){ExitThread();return;}
      if(!String.IsNullOrEmpty(state.cancelFile)&&File.Exists(state.cancelFile)){ExitThread();return;}
      if(!String.IsNullOrEmpty(state.leaseFile)&&(!File.Exists(state.leaseFile)||(DateTime.UtcNow-File.GetLastWriteTimeUtc(state.leaseFile)).TotalSeconds>2)){ExitThread();return;}
      bool preview=state.mode=="preview";
      Win.Point p;Win.GetCursorPos(out p);
      if(state.pinPointer){p.x=state.pointerX;p.y=state.pointerY;}
      if(preview){var r=Screen.PrimaryScreen.WorkingArea;double t=age.ElapsedMilliseconds/1300.0;p.x=r.Left+r.Width/2+(int)(Math.Sin(t)*r.Width*.15);p.y=r.Top+r.Height/2+(int)(Math.Cos(t*.8)*r.Height*.1);}
      cursor.Position(p.x-(int)(10*scale),p.y-(int)(8*scale));
      if(state.label!=lastLabel||state.mode!=lastMode){
        lastLabel=state.label;lastMode=state.mode;var r=Screen.PrimaryScreen.WorkingArea;
        using(var b=Art.Badge(state.label??"",preview,scale,state.mode))badge.Image(b,r.Left+(r.Width-b.Width)/2,r.Top+22);
      }
    }
    void Cancel(){try{if(state!=null&&!String.IsNullOrEmpty(state.cancelFile))File.WriteAllText(state.cancelFile,"cancelled");}catch{}ExitThread();}
    protected override void ExitThreadCore(){timer.Stop();hotkey.Dispose();foreach(var s in surfaces)s.Dispose();cursor.Dispose();badge.Dispose();pointer.Dispose();base.ExitThreadCore();}
  }
  internal sealed class HotkeyWindow:NativeWindow,IDisposable {
    readonly Action callback;internal bool Registered;
    internal HotkeyWindow(Action cancel){callback=cancel;CreateHandle(new CreateParams());Registered=Win.RegisterHotKey(Handle,7351,0x4003,0x1B);}
    protected override void WndProc(ref Message m){if(m.Msg==0x312){callback();return;}base.WndProc(ref m);}
    public void Dispose(){if(Registered)Win.UnregisterHotKey(Handle,7351);DestroyHandle();}
  }
}
