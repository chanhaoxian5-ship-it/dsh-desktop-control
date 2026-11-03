// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Web.Script.Serialization;

namespace DshDesktop {
  internal sealed class Request {
    public string command,hwnd,expectedTitle,expectedProcess,automationId,name,elementId,text,combo,outPath,cancelFile,expectText,feedbackFile,leaseFile;
    public int x,y,x2,y2,delta=1,ownerPid,expectedPid;
    public string button="left";
    public int count=1;
  }
  internal static class Driver {
    internal static object Describe(IntPtr h){
      Win.Rect r;Win.GetWindowRect(h,out r);uint pid;Win.GetWindowThreadProcessId(h,out pid);
      string process="";try{process=Process.GetProcessById((int)pid).ProcessName;}catch{}
      return new{hwnd="0x"+h.ToInt64().ToString("x"),title=Win.Title(h),process=process,pid=pid,
        foreground=Win.GetForegroundWindow()==h,rect=new{x=r.left,y=r.top,width=r.right-r.left,height=r.bottom-r.top}};
    }
    static IntPtr Target(Request r,bool foreground){
      if(String.IsNullOrWhiteSpace(r.hwnd))throw new Exception("Explicit target hwnd is required");
      long id=r.hwnd.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?Convert.ToInt64(r.hwnd.Substring(2),16):Int64.Parse(r.hwnd);
      var h=new IntPtr(id);
      if(!Win.IsWindow(h)||!Win.IsWindowVisible(h))throw new Exception("Target window no longer exists or is not visible");
      uint actualPid;Win.GetWindowThreadProcessId(h,out actualPid);
      if(r.expectedPid>0&&actualPid!=r.expectedPid)throw new Exception("Target process ID changed; stale window binding");
      if(r.expectedTitle!=null&&Win.Title(h)!=r.expectedTitle)throw new Exception("Target title changed; inspect again");
      if(r.expectedProcess!=null){uint pid;Win.GetWindowThreadProcessId(h,out pid);if(Process.GetProcessById((int)pid).ProcessName!=r.expectedProcess)throw new Exception("Target process mismatch");}
      if(foreground&&Win.GetForegroundWindow()!=h)throw new Exception("Target is not foreground; explicitly focus and verify it first");
      return h;
    }
    static void Cancel(Request r){
      if(!String.IsNullOrEmpty(r.cancelFile)&&File.Exists(r.cancelFile))throw new OperationCanceledException("Stopped by user");
      if(!String.IsNullOrEmpty(r.leaseFile)&&(!File.Exists(r.leaseFile)||(DateTime.UtcNow-File.GetLastWriteTimeUtc(r.leaseFile)).TotalSeconds>2))throw new OperationCanceledException("Controller heartbeat expired");
    }
    static void MarkActive(Request r,AutomationElement element=null){
      Cancel(r);if(String.IsNullOrEmpty(r.feedbackFile))throw new Exception("Visible feedback lease is required for input");
      var state=Program.Json.Deserialize<FeedbackState>(File.ReadAllText(r.feedbackFile));
      if(state==null||state.mode=="stop"||DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()>state.expiresAt)throw new Exception("Visual feedback lease expired; no input sent");
      state.mode="active";
      if(element!=null){var b=element.Current.BoundingRectangle;state.pinPointer=true;state.pointerX=(int)(b.X+b.Width/2);state.pointerY=(int)(b.Y+b.Height/2);}
      var temp=r.feedbackFile+"."+Guid.NewGuid().ToString("N")+".tmp";
      File.WriteAllText(temp,Program.Json.Serialize(state));File.Replace(temp,r.feedbackFile,null);
    }
    static void PointGuard(IntPtr h,int x,int y){
      Win.Rect bounds;Win.GetWindowRect(h,out bounds);
      if(x<bounds.left||x>=bounds.right||y<bounds.top||y>=bounds.bottom)throw new Exception("Point lies outside the target window");
      var hit=Win.WindowFromPoint(new Win.Point(x,y));var top=Win.GetAncestor(hit,2);
      if(top!=h&&hit!=h)throw new Exception("Another window is covering the point; inspect again");
    }
    static string RuntimeId(AutomationElement e){try{return String.Join(".",e.GetRuntimeId().Select(n=>n.ToString()).ToArray());}catch{return "";}}
    static List<AutomationElement> Elements(IntPtr h){
      var result=new List<AutomationElement>();var root=AutomationElement.FromHandle(h);
      var stack=new Stack<Tuple<AutomationElement,int>>();stack.Push(Tuple.Create(root,0));
      while(stack.Count>0&&result.Count<160){
        var next=stack.Pop();result.Add(next.Item1);if(next.Item2>=6)continue;
        var children=new List<AutomationElement>();
        try{for(var e=TreeWalker.ControlViewWalker.GetFirstChild(next.Item1);e!=null&&children.Count<100;e=TreeWalker.ControlViewWalker.GetNextSibling(e))children.Add(e);}catch(ElementNotAvailableException){}
        for(int i=children.Count-1;i>=0;i--)stack.Push(Tuple.Create(children[i],next.Item2+1));
      }return result;
    }
    static object Element(AutomationElement e){
      var c=e.Current;var b=c.BoundingRectangle;
      return new{elementId=RuntimeId(e),automationId=c.AutomationId,name=c.IsPassword?"[password control]":c.Name,
        role=c.ControlType.ProgrammaticName,enabled=c.IsEnabled,offscreen=c.IsOffscreen,password=c.IsPassword,
        rect=b.IsEmpty?null:new{x=b.X,y=b.Y,width=b.Width,height=b.Height}};
    }
    static AutomationElement Find(IntPtr h,Request r){
      if(String.IsNullOrEmpty(r.elementId)&&String.IsNullOrEmpty(r.automationId)&&String.IsNullOrEmpty(r.name))throw new Exception("Specify an elementId, automationId or exact control name from inspect");
      var matches=Elements(h).Where(e=>{
        try{return (String.IsNullOrEmpty(r.elementId)||RuntimeId(e)==r.elementId)&&
          (String.IsNullOrEmpty(r.automationId)||e.Current.AutomationId==r.automationId)&&
          (String.IsNullOrEmpty(r.name)||e.Current.Name==r.name);}catch{return false;}
      }).ToList();
      if(matches.Count!=1)throw new Exception("Selector matched "+matches.Count+" controls; refusing ambiguous/stale target");
      var element=matches[0];if(!element.Current.IsEnabled||element.Current.IsOffscreen)throw new Exception("Target control is unavailable");
      if(element.Current.IsPassword)throw new Exception("Password controls are not supported by this driver");
      return element;
    }
    internal static object Execute(Request r){
      Cancel(r);
      if(r.command=="health")return new{ok=true,user=System.Security.Principal.WindowsIdentity.GetCurrent().Name,pid=Process.GetCurrentProcess().Id,home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
      if(r.command=="active")return new{ok=true,window=Describe(Win.GetForegroundWindow())};
      if(r.command=="windows"){
        var windows=new List<object>();Win.EnumWindows((handle,data)=>{if(Win.IsWindowVisible(handle)&&!String.IsNullOrEmpty(Win.Title(handle))&&(Win.GetWindowLong(handle,-20)&0x80)==0)windows.Add(Describe(handle));return true;},IntPtr.Zero);
        return new{ok=true,windows=windows};
      }
      if(r.command=="hit-test"){var hit=Win.WindowFromPoint(new Win.Point(r.x,r.y));return new{ok=true,window=Describe(Win.GetAncestor(hit,2))};}
      if(r.command=="screenshot"){
        var rect=System.Windows.Forms.SystemInformation.VirtualScreen;
        string image;
        using(var b=new Bitmap(rect.Width,rect.Height))using(var g=Graphics.FromImage(b)){
          g.CopyFromScreen(rect.Left,rect.Top,0,0,b.Size);using(var stream=new MemoryStream()){b.Save(stream,ImageFormat.Png);image=Convert.ToBase64String(stream.ToArray());}
        }return new{ok=true,pngBase64=image,width=rect.Width,height=rect.Height,offset=new{x=rect.Left,y=rect.Top}};
      }
      var h=Target(r,false);
      if(r.command=="inspect")return new{ok=true,window=Describe(h),controls=Elements(h).Select(Element).ToArray(),limit=160};
      if(r.expectedPid<=0)throw new Exception("Mutating commands need expectedPid from inspect; no input sent");
      if(r.command=="focus"){
        MarkActive(r);
        if(Win.IsIconic(h))Win.ShowWindow(h,9);Win.SetForegroundWindow(h);Thread.Sleep(120);
        if(Win.GetForegroundWindow()!=h)throw new Exception("Windows refused focus; no input sent");
        return new{ok=true,window=Describe(h),verification=new{foreground=true}};
      }
      if(r.command=="invoke"||r.command=="set-value"){
        if(r.expectText!=null&&String.IsNullOrWhiteSpace(r.expectText))throw new Exception("Expected result text cannot be empty");
        var e=Find(h,r);object pattern;
        var bounds=e.Current.BoundingRectangle;
        if(bounds.IsEmpty)throw new Exception("Control has no visible bounds");
        PointGuard(h,(int)(bounds.X+bounds.Width/2),(int)(bounds.Y+bounds.Height/2));
        if(r.command=="set-value"){
          if(r.text==null)throw new Exception("set-value requires text");
          if(!e.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))throw new Exception("Control does not support ValuePattern; no coordinate fallback was attempted");
          var p=(ValuePattern)pattern;if(p.Current.IsReadOnly)throw new Exception("Control is read-only");
          Cancel(r);Target(r,false);MarkActive(r,e);p.SetValue(r.text);Cancel(r);
          string readback=p.Current.Value;bool verified=readback==r.text;
          return new{ok=verified,delivery="value-set",verification=new{valueMatches=verified},window=Describe(h)};
        }
        if(!e.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))throw new Exception("Control does not support InvokePattern; use an explicit checked click if appropriate");
        Cancel(r);Target(r,false);MarkActive(r,e);((InvokePattern)pattern).Invoke();
        bool? observed=null;
        if(r.expectText!=null){observed=false;for(int n=0;n<10;n++){Cancel(r);Thread.Sleep(60);if(Elements(h).Any(el=>el.Current.Name.Contains(r.expectText))){observed=true;break;}}}
        return new{ok=observed!=false,delivery="invoked",verification=new{expectedTextObserved=observed,applicationOutcome=observed==null?"not-verified":observed.Value?"verified":"not-observed"},window=Describe(h)};
      }
      Target(r,true);
      if(r.command=="click"||r.command=="move"||r.command=="scroll"||r.command=="drag"){
        if(r.button!="left"&&r.button!="right"&&r.button!="middle")throw new Exception("Invalid mouse button");
        if(r.count<1||r.count>2)throw new Exception("Click count must be 1 or 2");
        if(r.command=="scroll"&&(r.delta==0||Math.Abs((long)r.delta)>20))throw new Exception("Scroll delta must be between -20 and 20, excluding zero");
        PointGuard(h,r.x,r.y);if(r.command=="drag")PointGuard(h,r.x2,r.y2);
        MarkActive(r);Win.Move(r.x,r.y);Thread.Sleep(70);Cancel(r);Target(r,true);
        if(r.command=="click"){for(int n=0;n<r.count;n++){Cancel(r);Target(r,true);try{Win.MouseButton(false,r.button);Thread.Sleep(45);}finally{Win.MouseButton(true,r.button);}if(n<r.count-1)Thread.Sleep(90);}}
        if(r.command=="scroll")Win.Wheel(r.delta*120);
        if(r.command=="drag"){
          try{Win.MouseButton(false,r.button);for(int n=1;n<=20;n++){Cancel(r);Target(r,true);Win.Move(r.x+(r.x2-r.x)*n/20,r.y+(r.y2-r.y)*n/20);Thread.Sleep(20);}}
          finally{Win.MouseButton(true,r.button);}
        }
        Win.Point cursor;Win.GetCursorPos(out cursor);
        return new{ok=true,delivery="sent",cursor=new{x=cursor.x,y=cursor.y},verification=new{applicationOutcome="not-verified"},window=Describe(h)};
      }
      if(r.command=="type"){
        if(r.text==null)throw new Exception("type needs text");
        if(r.text.Length>2000)throw new Exception("type is limited to 2000 characters; use smaller verified steps");
        var focused=AutomationElement.FocusedElement;if(focused==null||focused.Current.IsPassword)throw new Exception("No eligible focused text control");
        MarkActive(r);
        foreach(char c in r.text.Replace("\r\n","\n").Replace("\r","\n")){
          Cancel(r);Target(r,true);
          if(c=='\n'||c=='\t'){ushort key=c=='\n'?(ushort)13:(ushort)9;try{Win.Key(key,false);}finally{Win.Key(key,true);}}
          else{try{Win.Unicode(c,false);}finally{Win.Unicode(c,true);}}
          Thread.Sleep(8);
        }
        return new{ok=true,delivery="sent",verification=new{applicationOutcome="not-verified"},window=Describe(h)};
      }
      if(r.command=="key"){
        var keys=ParseKeys(r.combo);var down=new List<ushort>();MarkActive(r);
        try{foreach(var key in keys){Cancel(r);Target(r,true);Win.Key(key,false);down.Add(key);Thread.Sleep(20);}}
        finally{for(int n=down.Count-1;n>=0;n--)Win.Key(down[n],true);}
        return new{ok=true,delivery="sent",verification=new{applicationOutcome="not-verified"},window=Describe(h)};
      }
      throw new Exception("Unknown command");
    }
    static List<ushort> ParseKeys(string combo){
      if(String.IsNullOrWhiteSpace(combo))throw new Exception("key needs combo");
      var map=new Dictionary<string,ushort>{{"ctrl",17},{"shift",16},{"alt",18},{"win",91},{"enter",13},{"tab",9},{"esc",27},{"backspace",8},{"delete",46},{"space",32},{"left",37},{"up",38},{"right",39},{"down",40},{"home",36},{"end",35}};
      var result=new List<ushort>();foreach(string raw in combo.Split('+')){
        var key=raw.Trim().ToLowerInvariant();ushort value;int number;
        if(map.TryGetValue(key,out value))result.Add(value);
        else if(key.Length==1&&Char.IsLetterOrDigit(key[0]))result.Add((ushort)Char.ToUpperInvariant(key[0]));
        else if(key.StartsWith("f")&&Int32.TryParse(key.Substring(1),out number)&&number>=1&&number<=12)result.Add((ushort)(111+number));
        else throw new Exception("Unknown key: "+key);
      }return result;
    }
  }
}
