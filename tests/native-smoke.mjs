import assert from 'node:assert/strict';
import { readFile, mkdtemp, rm, readdir, writeFile, utimes } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { spawnNative, nativeRequest, startFeedback, perform, stopAll, sessionRoot } from '../desktop.mjs';
const work=fileURLToPath(new URL('../.oil-ui/feedback/',import.meta.url));
process.env.DSH_DESKTOP_STATE_DIR=join(work,'sessions');
const temp=await mkdtemp(join(work,'native-test-'));
const readyPath=join(temp,'probe.json');
const fixture=spawnNative(['probe',readyPath,'40']);fixture.stdin.end();
const fixtureDone=new Promise(r=>fixture.once('close',r));
let fixtureLog='';fixture.stdout.setEncoding('utf8');fixture.stderr.setEncoding('utf8');
fixture.stdout.on('data',chunk=>{fixtureLog+=chunk});fixture.stderr.on('data',chunk=>{fixtureLog+=chunk});
const delay=ms=>new Promise(r=>setTimeout(r,ms));
try{
  for(let n=0;n<150&&!existsSync(readyPath);n++)await delay(25);
  assert.ok(existsSync(readyPath),'test surface did not open: '+fixtureLog+' exit='+fixture.exitCode+' ready='+readyPath);
  const {window}=JSON.parse(await readFile(readyPath,'utf8'));
  let out=await nativeRequest({command:'inspect',hwnd:window.hwnd});
  assert.equal(out.ok,true,JSON.stringify(out));
  const input=out.controls.find(e=>e.name==='Test input');
  const button=out.controls.find(e=>e.name==='Apply test');
  assert.ok(input&&button,'UI Automation did not expose expected controls');
  out=await nativeRequest({command:'set-value',hwnd:window.hwnd,expectedPid:window.pid+1,elementId:input.elementId,text:'must not write'});
  assert.equal(out.ok,false);assert.match(out.error,/process ID changed/);
  out=await perform({command:'focus',hwnd:window.hwnd,expectedPid:window.pid});
  const focusDeniedSafely=!out.ok&&/refused focus/.test(out.error);
  assert.ok(out.ok||focusDeniedSafely,JSON.stringify(out));
  out=await perform({command:'set-value',hwnd:window.hwnd,expectedPid:window.pid,elementId:input.elementId,text:'DSH 测试 123'});
  assert.equal(out.ok,true,JSON.stringify(out));assert.equal(out.verification.valueMatches,true);
  assert.equal(out.visual.shown,true);
  out=await perform({command:'invoke',hwnd:window.hwnd,expectedPid:window.pid,elementId:button.elementId,expectText:'Applied: DSH 测试 123'});
  assert.equal(out.ok,true,JSON.stringify(out));assert.equal(out.verification.expectedTextObserved,true);
  out=await nativeRequest({command:'click',hwnd:window.hwnd,expectedPid:window.pid,x:-100000,y:-100000});
  assert.equal(out.ok,false);assert.match(out.error,/outside|not foreground/);
  const foregroundBefore=(await nativeRequest({command:'active'})).window.hwnd;
  const visual=await startFeedback({preview:true,seconds:5,label:'隔离测试：不发送鼠标键盘输入'});
  try{
    const active=await nativeRequest({command:'active'});
    assert.equal(active.window.hwnd,foregroundBefore,'overlay stole focus');
    const hit=await nativeRequest({command:'hit-test',...visual.ready.badgePoint});
    assert.notEqual(hit.window.pid,visual.ready.pid,'overlay intercepted desktop hit testing');
    assert.equal(visual.ready.clickThrough,true);
    assert.equal(visual.ready.hotkeyRegistered,true);
    console.log(JSON.stringify({passed:true,controls:out.controls?.length,
      uiaValueVerified:true,uiaInvokeVerified:true,stalePidBlocked:true,outsidePointBlocked:true,
      foregroundPreserved:true,clickThroughVerified:true,focusDeniedSafely,captureExcluded:visual.ready.captureExcluded}));
  }finally{await visual.stop();}
  const pending=perform({command:'invoke',hwnd:window.hwnd,expectedPid:window.pid,elementId:button.elementId,expectText:'A deliberately absent result'});
  for(let n=0;n<100;n++){
    const sessions=await readdir(sessionRoot()).catch(()=>[]);
    let running=false;
    for(const id of sessions){try{const state=JSON.parse(await readFile(join(sessionRoot(),id,'state.json'),'utf8'));if(state.mode==='active')running=true}catch{}}
    if(running)break;await delay(15);
  }
  await stopAll();const stopped=await pending;
  assert.equal(stopped.ok,false);assert.equal(stopped.cancelled,true);
  const lease=await startFeedback({preview:true,seconds:8,label:'心跳清理验证'});
  try{
    await utimes(lease.leaseFile,new Date(0),new Date(0));
    await Promise.race([lease.done,delay(1500)]);
    assert.notEqual(lease.host.exitCode,null,'stale lease did not stop feedback host');
  }finally{await lease.stop();}
  assert.deepEqual(await readdir(sessionRoot()),[],'feedback session files remained');
  const capturePath=join(temp,'capture.png');
  const captured=await perform({command:'screenshot',outPath:capturePath});
  assert.equal(captured.ok,true);assert.equal(captured.pngBase64,undefined);
  assert.equal((await readFile(capturePath)).subarray(0,8).toString('hex'),'89504e470d0a1a0a');
  console.log(JSON.stringify({cancellationVerified:true,expiredLeaseCleanupVerified:true,sessionsCleaned:true,screenshotFileVerified:true}));
}finally{
  fixture.kill();
  await fixtureDone;
  await rm(temp,{recursive:true,force:true});
}
