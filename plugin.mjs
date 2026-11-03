// SPDX-License-Identifier: MIT
import { readFile, mkdir } from 'node:fs/promises';
import { join, dirname, isAbsolute, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { perform, stopAll, startFeedback } from './desktop.mjs';
const skillDirectory=fileURLToPath(new URL('./skills/desktop-control/',import.meta.url));
const defaultBackend={perform,stopAll,startFeedback};
const workspace=exec=>exec?.agent?.options?.cwd||process.cwd();
const mutationCommands=['focus','invoke','set-value','click','move','drag','scroll','type','key'];

export function registerDesktop(ctx,defineTool,backend=defaultBackend){
  const disposers=[],running=new Set();
  const add=(name,description,parameters,execute,concurrent=false)=>{
    const definition=defineTool({name,description,parameters,timeoutMs:30000,
      isConcurrencySafe:()=>concurrent,
      output:{schema:{type:'json'},render:(_args,value)=>[{type:'text',text:JSON.stringify(value)}]},
      async execute(args,exec){
        const controller=new AbortController();running.add(controller);
        const signal=exec?.signal?AbortSignal.any([exec.signal,controller.signal]):controller.signal;
        try{signal.throwIfAborted();return await execute(args,{...exec,agent:exec?.agent,signal});}
        catch(error){return {ok:false,error:error.message,applicationOutcome:'not-verified'}}
        finally{running.delete(controller)}
      }});
    const dispose=ctx.tools.register(definition);if(typeof dispose==='function')disposers.push(dispose);
  };
  add('desktop_observe','读取 Windows 窗口或 UI Automation 控件；截图返回文件路径，随后必须用 read_image 查看。不会移动鼠标或输入。',{
    mode:{type:'string',enum:['active','windows','inspect','screenshot'],required:true},
    hwnd:{type:'string'},outPath:{type:'string'}
  },async(args,exec)=>{
    const cwd=workspace(exec);
    let outPath=args.outPath;
    if(args.mode==='screenshot'){
      outPath=outPath?(isAbsolute(outPath)?outPath:resolve(cwd,outPath)):undefined;
      if(outPath)await mkdir(dirname(outPath),{recursive:true});
    }
    const result=await backend.perform({command:args.mode,hwnd:args.hwnd,outPath},{signal:exec.signal,workspace:cwd});
    return args.mode==='screenshot'?{...result,next:'Use read_image on the returned absolute path before coordinate actions'}:result;
  });
  add('desktop_action','在已确认的目标窗口执行一个可见动作：大指针、屏幕边缘发光、停止热键；优先按控件操作。必须传 inspect 得到的 hwnd 和 pid。ok/delivery 不等于应用业务成功，核对 verification。',{
    command:{type:'string',enum:mutationCommands,required:true},
    hwnd:{type:'string',required:true},expectedPid:{type:'integer',required:true},
    expectedTitle:{type:'string'},expectedProcess:{type:'string'},
    elementId:{type:'string'},automationId:{type:'string'},name:{type:'string'},
    text:{type:'string'},combo:{type:'string'},expectText:{type:'string'},
    button:{type:'string',enum:['left','right','middle']},count:{type:'integer'},
    x:{type:'integer'},y:{type:'integer'},x2:{type:'integer'},y2:{type:'integer'},delta:{type:'integer'}
  },(args,exec)=>backend.perform(args,{signal:exec.signal,workspace:workspace(exec)}));
  add('desktop_stop','停止本工作区通过本插件发起的桌面动作和可视提示；不会终止 DSH 或其他应用。',{},(_args,exec)=>backend.stopAll(workspace(exec)),true);
  add('desktop_preview','短暂预览大指针和屏幕边缘发光，不移动真实鼠标、不输入、不点击。',{
    seconds:{type:'integer'}
  },async(args,exec)=>{
    const seconds=Math.max(2,Math.min(15,args.seconds||6));
    const feedback=await backend.startFeedback({preview:true,seconds,workspace:workspace(exec),label:'仅预览提示层，没有输入或点击'});
    const abort=()=>{void feedback.stop()};exec.signal?.addEventListener('abort',abort,{once:true});
    try{await feedback.done;return {ok:true,preview:true,seconds,visual:feedback.ready}}
    finally{exec.signal?.removeEventListener('abort',abort);await feedback.stop()}
  });
  ctx.skills.registerProvider(()=>({name:'dsh-desktop-control',
    list:async()=>[{name:'desktop-control',description:'Windows 桌面控制：可见大指针与发光提示、按控件定位、窗口绑定、执行结果回读和可中断动作。',provider:'dsh-desktop-control',source:'bundled',rank:600,
      invocation:{modelInvocable:true,userInvocable:true},resourceBase:{kind:'directory',path:skillDirectory},path:join(skillDirectory,'SKILL.md'),locator:'desktop-control'}],
    get:async candidate=>candidate.locator==='desktop-control'?{...candidate,content:(await readFile(join(skillDirectory,'SKILL.md'),'utf8')).replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/u,'')}:undefined
  }));
  const dispose=()=>{for(const controller of running)controller.abort();for(const fn of disposers.splice(0).reverse())fn()};
  ctx.effect?.(()=>dispose);return dispose;
}
