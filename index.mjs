import { defineTool } from '@deepseek-ai/dsh-tools';
import { registerDesktop } from './plugin.mjs';
export const name='visible-desktop-control';
export const inject=['tools','skills'];
export function apply(ctx){return registerDesktop(ctx,defineTool);}
