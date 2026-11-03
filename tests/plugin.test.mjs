import test from 'node:test';
import assert from 'node:assert/strict';
import { registerDesktop } from '../plugin.mjs';
import { perform, startFeedback } from '../desktop.mjs';

function setup(backend={}){
  const tools=new Map();let provider;const calls=[];
  const defaults={perform:async(args,options)=>{calls.push({args,options});return {ok:true,delivery:'sent',verification:{applicationOutcome:'not-verified'}}},
    stopAll:async()=>({ok:true,stopRequests:1}),startFeedback:async()=>({done:Promise.resolve(),ready:{},stop:async()=>{}})};
  const dispose=registerDesktop({tools:{register:tool=>{tools.set(tool.name,tool);return()=>tools.delete(tool.name)}},
    skills:{registerProvider:fn=>{provider=fn()}},effect:()=>{}},value=>value,{...defaults,...backend});
  return {tools,provider,calls,dispose};
}
test('four tools and a non-conflicting skill are discoverable',async()=>{
  const {tools,provider}=setup();assert.equal(tools.size,4);
  const [entry]=await provider.list();assert.equal(entry.name,'desktop-control');
  assert.ok((await provider.get(entry)).content.includes('desktop_action'));
  assert.equal(await provider.get({locator:'other'}),undefined);
});
test('actions preserve window binding, parameters and cancellation signal',async()=>{
  const {tools,calls}=setup();const abort=new AbortController();const signal=abort.signal;
  const request={command:'set-value',hwnd:'0x123',expectedPid:123,text:'中文',elementId:'a'};
  const result=await tools.get('desktop_action').execute(request,{signal,agent:{options:{cwd:'D:/task'}}});
  assert.equal(result.ok,true);assert.equal(result.verification.applicationOutcome,'not-verified');
  assert.deepEqual(calls[0].args,request);assert.equal(calls[0].options.workspace,'D:/task');
  abort.abort();assert.equal(calls[0].options.signal.aborted,true);
});
test('an already aborted action does not reach the backend',async()=>{
  const {tools,calls}=setup();const abort=new AbortController();abort.abort();
  const result=await tools.get('desktop_action').execute({command:'click'},{signal:abort.signal});
  assert.equal(result.ok,false);assert.equal(calls.length,0);
});
test('backend refusal remains an explicit failure',async()=>{
  const {tools}=setup({perform:async()=>({ok:false,error:'stale PID',applicationOutcome:'not-verified'})});
  assert.equal((await tools.get('desktop_action').execute({command:'click'},{})).ok,false);
});
test('stop can run concurrently while actions remain exclusive',()=>{
  const {tools}=setup();assert.equal(tools.get('desktop_stop').isConcurrencySafe(),true);
  assert.equal(tools.get('desktop_action').isConcurrencySafe(),false);
});
test('preview clamps duration and cleans up its host',async()=>{
  let seconds,closed=false;
  const {tools}=setup({startFeedback:async options=>{seconds=options.seconds;return{done:Promise.resolve(),ready:{},stop:async()=>{closed=true}}}});
  assert.equal((await tools.get('desktop_preview').execute({seconds:999},{})).ok,true);
  assert.equal(seconds,15);assert.equal(closed,true);
});
test('silent mutations are refused before starting a native worker',async()=>{
  await assert.rejects(perform({command:'click'},{visible:false}),/require visible/);
});
test('non-Windows preview rejects without starting background work',async()=>{
  if(process.platform!=='win32')await assert.rejects(startFeedback(),/requires Windows/);
});
test('disposing the plugin cancels in-flight work and unregisters its tools',async()=>{
  const {tools,dispose}=setup({perform:async(_args,{signal})=>new Promise(resolve=>signal.addEventListener('abort',()=>resolve({ok:false,cancelled:true}),{once:true}))});
  const pending=tools.get('desktop_action').execute({command:'invoke'},{});
  await Promise.resolve();dispose();
  assert.equal((await pending).cancelled,true);assert.equal(tools.size,0);
});
