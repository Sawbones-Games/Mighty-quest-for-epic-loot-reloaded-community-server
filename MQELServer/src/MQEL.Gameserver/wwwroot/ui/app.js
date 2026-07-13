/* MQEL: Reloaded — Admin UI logic */
const $  = s => document.querySelector(s);
const esc = s => (s==null?'':String(s)).replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

/* ── admin token ── */
function saveTok(){ localStorage.setItem('mqTok', $('#tok').value); }
const hdr  = () => { const t=$('#tok').value; return t?{'X-Admin-Token':t}:{}; };
const jhdr = () => ({ ...hdr(), 'Content-Type':'application/json' });
function notice(el,msg,err){ if(el){ el.textContent=msg; el.className='mq-notice'+(err?' mq-notice-err':''); } }

/* ── cards ── */
function pill(label, running, sub){
  const st = running ? '<span class="mq-status mq-on" style="font-size:15px">Running</span>'
                     : '<span class="mq-status mq-off" style="font-size:15px">Stopped</span>';
  return `<div class="mq-card"><div class="mq-card-lbl">${esc(label)}</div><div class="mq-card-val">${st}</div><div class="mq-card-sub">${esc(sub||'')}</div></div>`;
}
function info(label, val, sub){
  return `<div class="mq-card"><div class="mq-card-lbl">${esc(label)}</div><div class="mq-card-val">${esc(val)}</div><div class="mq-card-sub">${esc(sub||'')}</div></div>`;
}

/* ── account editor ── */
const heroName = c => ({2:'Knight',3:'Archer',4:'Mage',5:'Runaway'}[c]||'—');
/* FTUE tutorial checkpoints, progression order — labels from game-data/.../Assignments folder names */
const ASSIGNMENTS = [
  [10,  'Intro movie'],
  [20,  'Pick starter castle'],
  [30,  'Name your castle'],
  [90,  'Choose first hero'],
  [100, 'Equip an item'],
  [5004,'Equip looted items'],
  [120, 'First castle — Forest'],
  [5003,'Castle crafting'],
  [125, 'Equip a skill'],
  [150, 'Second castle — Witch'],
];
const assignName = id => (ASSIGNMENTS.find(x=>x[0]===id)||[,'Assignment'])[1];
let aeAssign = new Set();
async function openAcct(id){
  const r = await fetch('/api/accounts/'+id, { headers:hdr() });
  if(!r.ok){ notice($('#err'),'Could not load account '+id,true); return; }
  const d = await r.json();
  $('#aeId').textContent = '#'+d.accountId;
  $('#aeName').value = d.displayName||''; $('#aeGold').value = d.gold||0; $('#aeLife').value = d.lifeForce||0;
  $('#aeLevel').value = d.heroLevel||1; $('#aeXp').value = d.heroXp||0;
  $('#aeHero').textContent = d.heroClass ? heroName(d.heroClass)+' · gear '+(d.gear||[]).length+' · spells '+(d.spells||[]).length : 'no hero yet';
  aeAssign = new Set(d.completedAssignments||[]);
  // FTUE checkpoints in progression order — names from the game's own Assignments spec folders.
  const ordered = [...ASSIGNMENTS.map(x=>x[0]),
    ...[...aeAssign].filter(id=>!ASSIGNMENTS.some(x=>x[0]===id)).sort((a,b)=>a-b)];  // + any extras the account carries
  $('#aeAssign').innerHTML = ordered.map(a =>
    `<label class="mq-toggle"><input type="checkbox" ${aeAssign.has(a)?'checked':''} onchange="toggleAssign(${a},this.checked)">${esc(assignName(a))} <span class="mq-toggle-id">#${a}</span></label>`).join('');
  notice($('#aeMsg'),'');
  $('#acctEditor').dataset.id = d.accountId; $('#acctEditor').style.display='';
}
function toggleAssign(a,on){ on?aeAssign.add(a):aeAssign.delete(a); }
function closeAcct(){ $('#acctEditor').style.display='none'; }
async function saveAcct(){
  const id = $('#acctEditor').dataset.id;
  const body = { displayName:$('#aeName').value, gold:+$('#aeGold').value||0, lifeForce:+$('#aeLife').value||0,
    heroLevel:+$('#aeLevel').value||1, heroXp:+$('#aeXp').value||0, completedAssignments:[...aeAssign] };
  const r = await fetch('/api/accounts/'+id, { method:'POST', headers:jhdr(), body:JSON.stringify(body) });
  notice($('#aeMsg'), r.ok ? "saved — applies on the account's next login." : r.status===401?'unauthorized.':'save failed.', !r.ok);
  if(r.ok) refresh();
}

/* Reset an account to a fresh first-run starter (wipes hero/gear/spells/gold/objectives/materials/progress)
   so the FTUE can be retested from scratch. Callable from a table row (id passed) or the open editor. */
async function resetAcct(id){
  id = id || $('#acctEditor').dataset.id;
  if(!id) return;
  if(!confirm('Reset account #'+id+' to a fresh starter?\n\nThis wipes the hero, gear, spells, gold, objectives, crafting materials and tutorial progress — the account boots like brand new. Relaunch the game afterwards.')) return;
  const r = await fetch('/api/accounts/'+id+'/reset', { method:'POST', headers:jhdr() });
  const msg = r.ok ? 'reset to starter — relaunch the game.' : r.status===401?'unauthorized — check the admin token.':'reset failed ('+r.status+').';
  notice($('#aeMsg'), msg, !r.ok);          // editor notice (visible when the editor is open)
  if(!r.ok && $('#acctEditor').style.display==='none') notice($('#err'), msg, true);  // surface failures from the row too
  if(r.ok){ closeAcct(); refresh(); }
}

/* ── save-states (template snapshots) ── */
async function loadSnaps(){
  const r = await fetch('/api/snapshots', { headers:hdr() });
  if(!r.ok){ $('#snapSection').style.display='none'; return; }
  const snaps = await r.json();
  $('#snapSection').style.display='';
  $('#snaps tbody').innerHTML = snaps.length
    ? snaps.map(s=>`<tr><td>${esc(s.name)}</td><td class="mq-muted">${esc(s.displayName||'')}</td><td>${heroName(s.heroClass)} ${s.heroClass?('Lv'+s.heroLevel):''}</td>
        <td><a class="snap-restore" data-name="${esc(s.name)}">load → live</a> · <a class="snap-delete" data-name="${esc(s.name)}">delete</a></td></tr>`).join('')
    : '<tr><td colspan="4" class="mq-muted" style="padding:11px">No save-states yet — capture the live account below.</td></tr>';
  // Bind via addEventListener + dataset (NOT inline onclick with the name interpolated into a JS string): inside an
  // attribute the browser decodes HTML entities before the JS parses, so a crafted snapshot name could execute. The
  // dataset value is pure data — read, never eval'd. (fableReview §2.7)
  $('#snaps tbody').querySelectorAll('.snap-restore').forEach(a=>a.addEventListener('click',()=>restoreSnap(a.dataset.name)));
  $('#snaps tbody').querySelectorAll('.snap-delete').forEach(a=>a.addEventListener('click',()=>deleteSnap(a.dataset.name)));
}
async function captureSnap(){
  const name = $('#snapName').value.trim();
  if(!name){ notice($('#snapMsg'),'type a name first (e.g. "after-witch"), then capture.',true); $('#snapName').focus(); return; }
  const r = await fetch('/api/snapshots', { method:'POST', headers:jhdr(), body:JSON.stringify({ name }) });
  notice($('#snapMsg'), r.ok?('captured "'+name+'" from the live account.'):r.status===401?'unauthorized — check the admin token.':'capture failed ('+r.status+').', !r.ok);
  if(r.ok){ $('#snapName').value=''; loadSnaps(); }
}
async function restoreSnap(name){
  if(!confirm('Overwrite the live dev account with snapshot "'+name+'"?')) return;
  const r = await fetch('/api/snapshots/'+encodeURIComponent(name)+'/restore', { method:'POST', headers:jhdr() });
  notice($('#snapMsg'), r.ok?'restored to live — relaunch the game.':'restore failed.', !r.ok);
  refresh();
}
async function deleteSnap(name){
  await fetch('/api/snapshots/'+encodeURIComponent(name), { method:'DELETE', headers:hdr() });
  loadSnaps();
}

/* ── logs ── */
async function refreshLogs(){
  const r = await fetch('/api/logs?lines=400', { headers:hdr() });
  $('#logBody').textContent = r.ok ? (await r.json()).lines.join('\n') : (r.status===401?'unauthorized':'(no log yet)');
  $('#logBody').scrollTop = $('#logBody').scrollHeight;
}

/* ── main poll ── */
async function refresh(){
  try{
    const r = await fetch('/api/status', { headers:hdr() });
    if(r.status===401){ notice($('#err'),'Unauthorized — enter the admin token.',true); return; }
    const s = await r.json();
    $('#cards').innerHTML =
      pill('Game Server', s.running, ':'+s.port+' · https') +
      info('Accounts', s.accounts, 'SQLite · '+esc(s.db||'mqel.db')) +
      info('Item Catalog', s.skus, s.templates+' templates') +
      info('Save-States', s.snapshots, 'templates');
    const a = await (await fetch('/api/accounts',{headers:hdr()})).json();
    $('#accts tbody').innerHTML = a.length
      ? a.map(x=>`<tr><td class="mq-muted">${x.accountId}</td><td>${esc(x.displayName)}</td><td>${heroName(x.heroClass)}</td>
          <td class="mq-lv">${x.heroClass?('Lv '+x.heroLevel):'—'}</td><td><a onclick="openAcct(${x.accountId})">edit</a> · <a onclick="resetAcct(${x.accountId})">reset</a></td></tr>`).join('')
      : '<tr><td colspan="5" class="mq-muted" style="padding:11px">No accounts yet — boot the game to create the dev account.</td></tr>';
    loadSnaps();
    notice($('#err'),'');
  }catch(e){ notice($('#err'),'Server unreachable: '+e,true); }
}

window.addEventListener('DOMContentLoaded', () => {
  $('#tok').value = localStorage.getItem('mqTok')||'';
  refresh(); refreshLogs();
  setInterval(refresh, 3000);
});
