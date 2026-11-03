// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DshDesktop {
  internal static class Program {
    internal static JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=67108864};
    [STAThread] static int Main(string[] args){
      Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);Win.Dpi();
      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
      try{
        if(args.Length>=2&&args[0]=="render"){Art.Preview(args[1]);return 0;}
        if(args.Length>=3&&args[0]=="overlay"){
          Application.EnableVisualStyles();Application.Run(new FeedbackContext(args[1],args[2]));return 0;
        }
        if(args.Length>=2&&args[0]=="probe"){
          Application.EnableVisualStyles();Probe(args[1],args.Length>2?Int32.Parse(args[2]):30);return 0;
        }
        if(args.Length==1&&args[0]=="request"){
          var request=Json.Deserialize<Request>(Console.In.ReadToEnd());if(request==null)throw new Exception("Empty request");
          using(var gate=new Mutex(false,"Local\\DshDesktopControl-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){
            bool entered=false;try{try{entered=gate.WaitOne(0);}catch(AbandonedMutexException){entered=true;}
              if(!entered)throw new Exception("Another desktop action is in progress; no request sent");
              var result=Driver.Execute(request);Console.WriteLine(Json.Serialize(result));
              var ok=result.GetType().GetProperty("ok");return ok!=null&&Object.Equals(ok.GetValue(result,null),false)?2:0;
            }finally{if(entered)gate.ReleaseMutex();}
          }
        }
        throw new Exception("Use request, overlay STATE READY, render FILE.png, or probe READY [seconds]");
      }catch(Exception e){Console.WriteLine(Json.Serialize(new{ok=false,error=e.Message,cancelled=e is OperationCanceledException,applicationOutcome="not-verified"}));return 2;}
    }
    static void Probe(string ready,int seconds){
      using(var form=new Form()){
        form.Text="DSH Desktop Control — isolated test window";form.Size=new Size(720,430);form.StartPosition=FormStartPosition.CenterScreen;
        form.TopMost=true;
        var input=new TextBox{Location=new Point(36,72),Size=new Size(600,32),Name="DshTestInput",AccessibleName="Test input",Font=new Font("Segoe UI",13)};
        var button=new Button{Location=new Point(36,134),Size=new Size(180,48),Name="DshTestApply",AccessibleName="Apply test",Text="Apply test"};
        var label=new Label{Location=new Point(36,218),Size=new Size(620,54),AccessibleName="Ready",Text="Ready",Font=new Font("Segoe UI",13)};
        var heading=new Label{Location=new Point(36,24),Size=new Size(620,32),Text="Owned test surface — no other app is modified",Font=new Font("Segoe UI",12)};
        button.Click+=(s,e)=>{label.Text="Applied: "+input.Text;label.AccessibleName=label.Text;};
        form.Controls.AddRange(new Control[]{heading,input,button,label});
        var timer=new System.Windows.Forms.Timer{Interval=Math.Max(1,seconds)*1000};timer.Tick+=(s,e)=>form.Close();
        form.Shown+=(s,e)=>{Win.ShowWindow(form.Handle,5);form.Activate();input.Focus();File.WriteAllText(ready,Json.Serialize(new{window=Driver.Describe(form.Handle),pid=Process.GetCurrentProcess().Id}));timer.Start();};
        Application.Run(form);timer.Dispose();
      }
    }
  }
}
