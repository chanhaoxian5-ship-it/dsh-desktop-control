// SPDX-License-Identifier: MIT
import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile, rename, rm, readdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root=fileURLToPath(new URL('./',import.meta.url));
const executable=join(root,'bin/DshDesktop.exe');
const labels={focus:'正在切换到指定窗口',click:'正在点击',move:'正在移动指针',drag:'正在拖动',scroll:'正在滚动',
  type:'正在输入文字',key:'正在发送快捷键',invoke:'正在操作指定控件','set-value':'正在填写指定输入框'};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
export const sessionRoot=(workspace=process.cwd())=>process.env.DSH_DESKTOP_STATE_DIR||join(workspace,'.dsh-desktop-runtime','sessions');
async function atomic(path,value){const temp=path+'.'+randomUUID()+'.tmp';await writeFile(temp,JSON.stringify(value),'utf8');await rename(temp,path);}
export function spawnNative(args,options={}){
  if(process.platform!=='win32')throw Error('This desktop backend requires Windows');
  if(!existsSync(executable))throw Error('Native executable is missing; run pwsh -NoProfile -File scripts/build.ps1');
  return spawn(executable,args,{windowsHide:true,stdio:['pipe','pipe','pipe'],...options});
}
export async function nativeRequest(request,{signal,timeout=20000,cancelFile}={}){
  signal?.throwIfAborted();
  return new Promise((resolveResult,reject)=>{
    const worker=spawnNative(['request']);let output='',error='',abortTimer,ended=false,expired=false,stopping=false;
    const stop=async()=>{
      if(ended||stopping)return;stopping=true;
      if(cancelFile)await writeFile(cancelFile,'cancelled').catch(()=>{});
      abortTimer=setTimeout(()=>worker.kill(),800);
    };
    const timer=setTimeout(()=>{expired=true;void stop()},timeout);
    const abort=()=>{void stop()};signal?.addEventListener('abort',abort,{once:true});
    const poll=cancelFile?setInterval(()=>{if(existsSync(cancelFile))void stop()},80):null;
    worker.stdout.setEncoding('utf8');worker.stderr.setEncoding('utf8');
    worker.stdout.on('data',chunk=>{output+=chunk});
    worker.stderr.on('data',chunk=>{error+=chunk});
    worker.on('error',reject);
    worker.on('close',()=>{
      ended=true;clearTimeout(timer);clearTimeout(abortTimer);if(poll)clearInterval(poll);signal?.removeEventListener('abort',abort);
      if(expired||signal?.aborted||(cancelFile&&existsSync(cancelFile))){resolveResult({ok:false,cancelled:true,error:expired?'Action timed out':'Action stopped',applicationOutcome:'unknown; do not blindly retry'});return;}
      try{resolveResult(JSON.parse(output.trim()))}catch{reject(Error(error||output||'Desktop worker ended without a result'))}
    });
    worker.stdin.end(JSON.stringify(request));
  });
}
export async function startFeedback({preview=false,seconds=30,label='正在操作',workspace}={}){
  if(process.platform!=='win32')throw Error('This desktop backend requires Windows');
  if(!existsSync(executable))throw Error('Native executable is missing; run scripts/build.ps1');
  const directory=join(sessionRoot(workspace),randomUUID());await mkdir(directory,{recursive:true});
  const statePath=join(directory,'state.json'),readyPath=join(directory,'ready.json'),cancelFile=join(directory,'cancel.flag'),leaseFile=join(directory,'lease.flag');
  await writeFile(leaseFile,String(Date.now()));
  const state={mode:preview?'preview':'preparing',label,ownerPid:process.pid,expiresAt:Date.now()+seconds*1000,cancelFile,leaseFile};
  await atomic(statePath,state);
  const host=spawnNative(['overlay',statePath,readyPath]);host.stdin.end();
  const heartbeat=setInterval(()=>{void writeFile(leaseFile,String(Date.now())).catch(()=>{})},400);
  let error='';host.stderr.on('data',b=>{error+=b.toString('utf8')});host.stdout.on('data',b=>{error+=b.toString('utf8')});
  let closing=false;
  const done=new Promise(resolveExit=>{host.once('exit',resolveExit);host.once('error',resolveExit)});
  host.on('exit',()=>{if(!closing&&!preview)void writeFile(cancelFile,'visual feedback ended').catch(()=>{})});
  const stop=async()=>{
    closing=true;clearInterval(heartbeat);await atomic(statePath,{...state,mode:'stop'}).catch(()=>{});
    await Promise.race([done,delay(1200)]);
    if(host.exitCode===null)host.kill();
    await rm(directory,{recursive:true,force:true}).catch(()=>{});
  };
  for(let n=0;n<120;n++){
    if(existsSync(readyPath)){
      let ready;try{ready=JSON.parse(await readFile(readyPath,'utf8'))}catch{await delay(25);continue;}
      if(!ready.hotkeyRegistered){await stop();throw Error('Another visual-control session owns the stop hotkey; no action started');}
      return {host,ready,directory,cancelFile,statePath,leaseFile,done,stop,
        phase:async(mode,label)=>{const latest=JSON.parse(await readFile(statePath,'utf8'));Object.assign(state,latest,{mode,label});await atomic(statePath,state)}};
    }
    if(host.exitCode!==null){await stop();throw Error(error||'Feedback host failed to start');}
    await delay(25);
  }
  await stop();throw Error('Feedback host did not become ready; no input was sent');
}
export async function perform(request,{signal,visible=true,timeout=20000,workspace}={}){
  signal?.throwIfAborted();
  const mutating=Object.hasOwn(labels,request.command);
  let feedback;
  try{
    if(mutating&&visible)feedback=await startFeedback({label:labels[request.command],seconds:Math.ceil(timeout/1000)+5,workspace});
    if(mutating&&!visible)throw Error('Mutating desktop actions require visible feedback');
    const result=await nativeRequest({...request,ownerPid:process.pid,cancelFile:feedback?.cancelFile,feedbackFile:feedback?.statePath,leaseFile:feedback?.leaseFile},
      {signal,timeout,cancelFile:feedback?.cancelFile});
    if(request.command==='screenshot'&&result.ok&&result.pngBase64){
      const path=request.outPath||join(process.env.LOCALAPPDATA||join(homedir(),'AppData','Local'),'DshDesktopControl','captures',randomUUID()+'.png');
      await mkdir(dirname(path),{recursive:true});await writeFile(path,Buffer.from(result.pngBase64,'base64'),{flag:'wx'});
      delete result.pngBase64;result.path=resolve(path);
    }
    if(feedback){
      result.visual={shown:true,hotkeyRegistered:feedback.ready.hotkeyRegistered,captureExcluded:feedback.ready.captureExcluded};
      const verified=result.verification?.valueMatches===true||result.verification?.expectedTextObserved===true||result.verification?.foreground===true;
      await feedback.phase(!result.ok?'error':verified?'verified':'sent',!result.ok?'已停止；请核对当前界面':verified?'已回读核对指定结果':'输入已发送，应用结果待核对').catch(()=>{});
      await delay(250);
    }
    return result;
  }finally{if(feedback)await feedback.stop();}
}
export async function stopAll(workspace){
  let count=0;for(const name of await readdir(sessionRoot(workspace)).catch(()=>[])){
    if(!/^[a-f0-9-]{36}$/u.test(name))continue;
    await writeFile(join(sessionRoot(workspace),name,'cancel.flag'),'stop requested').catch(()=>{});count++;
  }return {ok:true,stopRequests:count};
}
async function main(args){
  if(args[0]==='preview'){
    const seconds=Math.min(30,Math.max(2,Number(args[1])||8));
    const feedback=await startFeedback({preview:true,seconds,label:'仅预览提示层，没有输入或点击'});
    console.log(JSON.stringify({ok:true,preview:true,seconds,...feedback.ready}));
    try{await Promise.race([feedback.done,delay((seconds+2)*1000)])}finally{await feedback.stop()}
  }else if(args[0]==='stop'){console.log(JSON.stringify(await stopAll()))}
  else if(args[0]==='request-file'&&args[1]){const result=await perform(JSON.parse(await readFile(resolve(args[1]),'utf8')));console.log(JSON.stringify(result));if(!result.ok)process.exitCode=2}
  else if(args[0]==='active'){console.log(JSON.stringify(await perform({command:'active'})))}
  else{console.log('Usage: node desktop.mjs preview [seconds] | stop | active | request-file request.json')}
}
if(process.argv[1]&&pathToFileURL(resolve(process.argv[1])).href===import.meta.url){
  try{await main(process.argv.slice(2))}catch(error){console.error(error.message);process.exitCode=1}
}
