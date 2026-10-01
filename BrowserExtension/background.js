const HOST = 'com.bpet.sessions';
let busy = false, pending = false;
const safe = url => /^https?:\/\//i.test(url || '');
async function sync() {
  if (busy) { pending = true; return; }
  busy = true;
  try {
    const saved = await chrome.storage.local.get(['profile', 'done']);
    const profile = saved.profile || crypto.randomUUID();
    if (!saved.profile) await chrome.storage.local.set({ profile });
    const browser = /Edg\//.test(navigator.userAgent) ? 'edge' : /coc_coc_browser/i.test(navigator.userAgent) ? 'coccoc' : 'chrome';
    const windows = (await chrome.windows.getAll({ populate: true, windowTypes: ['normal'] }))
      .filter(w => !w.incognito).map(w => ({ tabs: (w.tabs || []).filter(t => !t.incognito && safe(t.url)).map(t => ({ url: t.url, title: t.title || '', pinned: !!t.pinned, active: !!t.active })) })).filter(w => w.tabs.length);
    const response = await chrome.runtime.sendNativeMessage(HOST, { profile, snapshot: { profile, browser, at: new Date().toISOString(), windows } });
    const job = response.job;
    if (job) {
      let message = 'Phiên đã được mở trước đó.';
      if (!(saved.done || []).includes(job.id)) {
        // Record first to prevent duplicate windows if the browser/host restarts mid-command.
        await chrome.storage.local.set({ done: [...(saved.done || []).slice(-99), job.id] });
        let total = 0;
        try {
          for (const group of job.windows) {
            const tabs = group.tabs.filter(t => safe(t.url));
            if (!tabs.length) continue;
            const window = await chrome.windows.create({ url: tabs.map(t => t.url), focused: false });
            const created = await chrome.tabs.query({ windowId: window.id });
            for (let i = 0; i < created.length; i++) {
              await chrome.tabs.update(created[i].id, { pinned: tabs[i].pinned, ...(tabs[i].active ? { active: true } : {}) });
            }
            total += tabs.length;
          }
          message = `Đã mở ${total} tab trong đúng profile trình duyệt.`;
        } catch (error) { message = `Mới mở ${total} tab; có lỗi: ${error.message}. Không tự lặp lại để tránh trùng tab.`; }
      }
      await chrome.runtime.sendNativeMessage(HOST, { profile, result: { id: job.id, profile, at: new Date().toISOString(), message } });
    }
    await chrome.action.setBadgeText({ text: '' });
    await chrome.action.setTitle({ title: 'BPet — Đã lưu phiên trên máy' });
  } catch (error) {
    await chrome.action.setBadgeText({ text: '!' });
    await chrome.action.setTitle({ title: 'BPet — Hãy mở BPet để kết nối: ' + error.message });
  } finally {
    busy = false;
    if (pending) { pending = false; setTimeout(sync, 1000); }
  }
}
let debounce;
function schedule() { clearTimeout(debounce); debounce = setTimeout(sync, 1200); }
chrome.tabs.onCreated.addListener(schedule);
chrome.tabs.onUpdated.addListener(schedule);
chrome.tabs.onRemoved.addListener(schedule);
chrome.tabs.onMoved.addListener(schedule);
chrome.tabs.onActivated.addListener(schedule);
chrome.windows.onRemoved.addListener(schedule);
chrome.action.onClicked.addListener(sync);
chrome.runtime.onStartup.addListener(sync);
chrome.runtime.onInstalled.addListener(() => { chrome.alarms.create('bpet-session', { periodInMinutes: 0.5 }); sync(); });
chrome.alarms.onAlarm.addListener(a => { if (a.name === 'bpet-session') sync(); });
chrome.alarms.create('bpet-session', { periodInMinutes: 0.5 });
