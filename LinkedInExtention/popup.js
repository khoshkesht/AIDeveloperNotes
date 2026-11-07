const sendButton = document.getElementById("send-post");
const status = document.getElementById("status");
let activePost = null;
let busy = false;

async function initialize() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const { posts = [], lastPosted = 0, activePost: active = null } = await chrome.storage.local.get([
      "posts", "lastPosted", "activePost"
    ]);
    activePost = active;
    const isLinkedIn = tab?.url && new URL(tab.url).hostname === "www.linkedin.com";
    const next = Posts.nextPost(posts, lastPosted);
    document.getElementById("summary").textContent = `آخرین انتشار ثبت‌شده: ${lastPosted} | پست بعدی: ${next?.number ?? "موجود نیست"}`;
    sendButton.disabled = busy || !isLinkedIn || !next || Boolean(active);
    document.getElementById("active-post").hidden = !active;
    const stalePublish = active?.state === "publishing" && Date.now() - active.publishStartedAt > 60_000;
    document.getElementById("mark-published").hidden = !["ready", "attention"].includes(active?.state) && !stalePublish;
    document.getElementById("mark-published").disabled = busy;
    document.getElementById("cancel-draft").disabled = busy || (active?.state === "publishing" && !stalePublish);
    if (active) {
      const labels = {
        loading: `در حال آماده‌سازی پست ${active.number}…`,
        countdown: `پست ${active.number} پس از شمارش معکوس ۵ ثانیه‌ای خودکار منتشر می‌شود. برای توقف، لغو را بزنید.`,
        publishing: stalePublish ? "نتیجهٔ انتشار نامشخص است؛ لینکدین را بررسی کنید و در صورت انتشار، شماره را ثبت کنید."
          : `در حال انتشار پست ${active.number} و دریافت تأیید لینکدین…`,
        attention: `${active.error || "انتشار تأیید نشد."} فقط اگر پست واقعاً منتشر شده است، ثبت انتشار را بزنید.`,
        ready: `پست ${active.number} از نسخهٔ قبلی آماده مانده است؛ ثبت دستی یا لغو کنید.`
      };
      document.getElementById("active-summary").textContent = labels[active.state] || "وضعیت پست را بررسی کنید.";
    }
    status.textContent = !posts.length ? "ابتدا پوشهٔ پست‌ها را از تنظیمات انتخاب کنید."
      : !isLinkedIn ? "برای آماده‌سازی پست، یک صفحه از سایت لینکدین را باز کنید."
      : "پس از آماده شدن متن، شمارش معکوس ۵ ثانیه‌ای آغاز و پست خودکار منتشر می‌شود.";
  } catch {
    status.textContent = "امکان بررسی تنظیمات وجود ندارد. دوباره تلاش کنید.";
  }
}

async function runAction(type) {
  if (busy) return;
  busy = true;
  sendButton.disabled = true;
  document.getElementById("mark-published").disabled = true;
  document.getElementById("cancel-draft").disabled = true;
  try {
    const result = await chrome.runtime.sendMessage({ type, number: activePost?.number });
    if (!result?.ok) throw new Error(result?.error || "عملیات انجام نشد.");
    if (type === "OPEN_POST") window.close();
    busy = false;
    await initialize();
    if (type === "MARK_PUBLISHED") status.textContent = "شمارهٔ انتشار ذخیره شد. پست بعدی آمادهٔ انتخاب است.";
    if (type === "CANCEL_DRAFT") status.textContent = "پیگیری لغو شد؛ شمارهٔ آخرین انتشار تغییر نکرد. متن داخل لینکدین پاک نمی‌شود.";
  } catch (error) {
    busy = false;
    await initialize();
    status.textContent = error.message;
  }
}

sendButton.addEventListener("click", () => runAction("OPEN_POST"));
document.getElementById("mark-published").addEventListener("click", () => runAction("MARK_PUBLISHED"));
document.getElementById("cancel-draft").addEventListener("click", () => runAction("CANCEL_DRAFT"));
document.getElementById("settings").addEventListener("click", () => chrome.runtime.openOptionsPage());
chrome.storage.onChanged.addListener((_changes, area) => {
  if (area === "local" && !busy) initialize();
});
initialize();
