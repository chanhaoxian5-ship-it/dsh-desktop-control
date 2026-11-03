const {createRequire}=require('node:module');
const {join}=require('node:path');
const {pathToFileURL}=require('node:url');
(async()=>{
 if(!process.env.DSH_RUNTIME_ROOT)throw Error('Set DSH_RUNTIME_ROOT to the installed DSH runtime directory');
 const req=createRequire(join(process.env.DSH_RUNTIME_ROOT,'package.json'));
 const load=name=>import(pathToFileURL(req.resolve(name)).href);
 const {Context}=await load('@deepseek-ai/cordis');
 const SystemPrompt=(await load('@deepseek-ai/dsh-system-prompt')).default;
 const Tools=await load('@deepseek-ai/dsh-tools');
 const Skills=(await load('@deepseek-ai/dsh-skill')).default;
 const {registerDesktop}=await import(pathToFileURL(join(__dirname,'../plugin.mjs')).href);
 const ctx=new Context();await ctx.plugin(SystemPrompt);await ctx.plugin(Tools.default);await ctx.plugin(Skills);
 registerDesktop(ctx,Tools.defineTool);
 const names=['desktop_observe','desktop_action','desktop_stop','desktop_preview'];
 for(const name of names)if(!ctx.tools.get(name))throw Error('Missing tool '+name);
 const skill=await ctx.skills.get('desktop-control');if(!skill?.content)throw Error('Missing skill');
 console.log(JSON.stringify({registeredTools:names,skillLoaded:true,nativeActionsExecuted:false}));
})().catch(error=>{console.error(error);process.exitCode=1});
