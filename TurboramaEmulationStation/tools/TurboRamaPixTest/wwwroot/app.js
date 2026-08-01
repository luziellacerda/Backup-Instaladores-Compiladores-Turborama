const state = { charge: null, options: null };

const money = cents => (cents / 100).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const byId = id => document.getElementById(id);

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || 'Falha na operação.');
  return data;
}

function drawTestQr(value) {
  const canvas = byId('test-qr');
  const ctx = canvas.getContext('2d');
  const cells = 29;
  const size = canvas.width / cells;
  let seed = 0;
  for (const char of value) seed = (seed * 31 + char.charCodeAt(0)) >>> 0;
  const next = () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296;
  ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#10131e';
  for (let y = 0; y < cells; y++) for (let x = 0; x < cells; x++) {
    if (next() > .53) ctx.fillRect(x * size, y * size, Math.ceil(size), Math.ceil(size));
  }
  const finder = (x, y) => {
    ctx.fillStyle = '#10131e'; ctx.fillRect(x * size, y * size, 7 * size, 7 * size);
    ctx.fillStyle = '#ffffff'; ctx.fillRect((x + 1) * size, (y + 1) * size, 5 * size, 5 * size);
    ctx.fillStyle = '#10131e'; ctx.fillRect((x + 2) * size, (y + 2) * size, 3 * size, 3 * size);
  };
  finder(1, 1); finder(cells - 8, 1); finder(1, cells - 8);
}

async function refresh() {
  const [counter, logs] = await Promise.all([api('/api/counter'), api('/api/logs')]);
  byId('remaining').textContent = counter.remainingLabel;
  byId('counter-state').textContent = counter.active ? `Ativo • ${counter.secondsPerTick}×` : 'Pausado';
  byId('logs').innerHTML = logs.map(log => `<li><time>${new Date(log.at).toLocaleTimeString('pt-BR')}</time>${escapeHtml(log.message)}</li>`).join('');
}

function escapeHtml(value) {
  return value.replace(/[&<>'"]/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&#39;', '"':'&quot;' }[c]));
}

async function loadOptions() {
  state.options = await api('/api/options');
  byId('packages').innerHTML = state.options.allowedMinutes.map(minutes => {
    const amount = minutes * state.options.priceCentsPerMinute;
    return `<button class="package" data-minutes="${minutes}"><strong>${minutes}</strong><span>minutos</span><em>${money(amount)}</em></button>`;
  }).join('');
  document.querySelectorAll('.package').forEach(button => button.addEventListener('click', () => createCharge(Number(button.dataset.minutes))));
}

async function createCharge(minutes) {
  try {
    state.charge = await api('/api/charges', { method: 'POST', body: JSON.stringify({ minutes, sessionId: byId('session-id').value }) });
    byId('payment-title').textContent = `${state.charge.minutes} minutos de jogo`;
    byId('payment-amount').textContent = money(state.charge.amountCents);
    byId('payment-description').textContent = `Sessão: ${state.charge.sessionId}. Expira às ${new Date(state.charge.expiresAt).toLocaleTimeString('pt-BR')}.`;
    byId('pix-code').value = state.charge.testPixCode;
    drawTestQr(state.charge.testPixCode);
    byId('step-select').classList.add('hidden');
    byId('step-payment').classList.remove('hidden');
  } catch (error) { alert(error.message); }
}

byId('approve-payment').addEventListener('click', async () => {
  if (!state.charge) return;
  try {
    const result = await api(`/api/charges/${state.charge.id}/simulate-approval`, { method: 'POST' });
    alert(result.message);
    byId('step-payment').classList.add('hidden');
    byId('step-select').classList.remove('hidden');
    state.charge = null;
    await refresh();
  } catch (error) { alert(error.message); }
});

byId('copy-code').addEventListener('click', async () => {
  await navigator.clipboard.writeText(byId('pix-code').value);
  byId('copy-code').textContent = 'Código copiado';
  setTimeout(() => byId('copy-code').textContent = 'Copiar código', 1400);
});

byId('cancel-payment').addEventListener('click', () => {
  state.charge = null;
  byId('step-payment').classList.add('hidden');
  byId('step-select').classList.remove('hidden');
});

byId('start-counter').addEventListener('click', () => api('/api/counter/start', { method: 'POST' }).then(refresh));
byId('pause-counter').addEventListener('click', () => api('/api/counter/pause', { method: 'POST' }).then(refresh));
byId('reset-counter').addEventListener('click', () => api('/api/counter/reset', { method: 'POST' }).then(refresh));
byId('speed').addEventListener('change', event => api('/api/counter/speed', { method: 'POST', body: JSON.stringify({ secondsPerTick: Number(event.target.value) }) }).then(refresh));

loadOptions().then(refresh);
setInterval(refresh, 1000);
