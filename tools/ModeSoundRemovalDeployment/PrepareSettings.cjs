// One-off deployment preparation, never loaded by Receiver. Preserve all bytes
// outside the removed top-level property, including number lexemes and arrays.
const fs = require('node:fs');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
function removeProperty(bytes) {
  const bom = bytes.subarray(0, 3).equals(Buffer.from([239,187,191]));
  const text = new TextDecoder('utf-8', {fatal:true}).decode(bytes.subarray(bom ? 3 : 0));
  const parsed = JSON.parse(text);
  if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') throw new Error('Settings root must be an object');
  let i=0, previousComma=null;
  const spaces=()=>{while (/\s/.test(text[i] || '') && i<text.length) i++;};
  const stringEnd=(start)=>{
    let p=start+1;
    while(p<text.length) {if(text[p]==='\\') p+=2;else if(text[p++]==='"') return p;}
    throw new Error('Unterminated JSON string');
  };
  spaces();if(text[i++]!=='{')throw new Error('Expected object');
  const properties=[];
  while(true) {
    spaces();if(text[i]==='}')break;
    const start=i, keyEnd=stringEnd(i), key=JSON.parse(text.slice(i,keyEnd));i=keyEnd;spaces();
    if(text[i++]!==':')throw new Error('Expected colon');spaces();
    let depth=0;
    while(i<text.length) {
      const c=text[i];
      if(c==='"'){i=stringEnd(i);continue;}
      if(depth===0 && (c===',' || c==='}'))break;
      if(c==='{' || c==='[')depth++;
      else if(c==='}' || c===']')depth--;
      i++;
    }
    let end=i;while(end>start && /\s/.test(text[end-1]))end--;
    properties.push({key,start,end,previousComma,nextComma:text[i]===','?i:null});
    if(text[i]!==',')break;previousComma=i++;
  }
  const matches=properties.filter(p=>p.key==='modeSound');
  if(matches.length>1)throw new Error('Duplicate top-level retired property; refusing ambiguous cleanup');
  if(!matches.length)return {bytes,removed:false,retainedOutsideRemovedSpan:true};
  const p=matches[0];
  const start=p.nextComma!==null ? p.start : (p.previousComma ?? p.start);
  const end=p.nextComma!==null ? p.nextComma+1 : p.end;
  const cleaned=text.slice(0,start)+text.slice(end);
  const expected={...parsed};delete expected.modeSound;
  assert.deepEqual(JSON.parse(cleaned),expected);
  const output=Buffer.concat([bom?Buffer.from([239,187,191]):Buffer.alloc(0),Buffer.from(cleaned,'utf8')]);
  return {bytes:output,removed:true,retainedOutsideRemovedSpan:true,removedSpan:{start,end}};
}
function selfTest() {
  for(const text of [
    '{"modeSound":{},"a":900719925474099312345,"b":[null,true,{"q":"汉字"}]}',
    '{"a":1.2300e+19,"modeSound":{"volumePercent":90},"unknown":{"modeSound":42}}',
    '{"a":[1,2],"modeSound":false}', '{"modeSound":null}',
    '{"mode\\u0053ound":{"x":"},\\\""},"a":-0.00}', '{"a":123}'
  ]) {
    for(const prefix of [Buffer.alloc(0),Buffer.from([239,187,191])]) {
      const input=Buffer.concat([prefix,Buffer.from(text)]);const r=removeProperty(input);
      if(!r.removed)assert.deepEqual(r.bytes,input);
      if(text.includes('900719925474099312345'))assert(r.bytes.includes(Buffer.from('900719925474099312345')));
      if(text.includes('1.2300e+19'))assert(r.bytes.includes(Buffer.from('1.2300e+19')));
    }
  }
  assert.throws(()=>removeProperty(Buffer.from('{"modeSound":1,"modeSound":2}')));
  console.log('PASS 12 isolated settings fixtures plus duplicate-key refusal; no formal file writes');
}
if(process.argv[2]==='--self-test') {selfTest();process.exit(0);}
const [source,destination,recordPath]=process.argv.slice(2);
if(!recordPath)throw new Error('Expected source, prepared copy and record paths');
const original=fs.readFileSync(source),result=removeProperty(original);
fs.writeFileSync(destination,result.bytes,{flag:'wx'});
const hash=b=>crypto.createHash('sha256').update(b).digest('hex').toUpperCase();
fs.writeFileSync(recordPath,JSON.stringify({source,destination,removedTopLevelProperty:result.removed?'modeSound':null,
  rewrittenFormalFile:false,oldSha256:hash(original),preparedSha256:hash(result.bytes),
  retainedOutsideRemovedSpan:result.retainedOutsideRemovedSpan,removedSpan:result.removedSpan??null},null,2),{flag:'wx'});
console.log(result.removed?'PASS prepared top-level property removal only':'PASS property absent; original bytes retained');
