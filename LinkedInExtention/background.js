importScripts("posts.js");

const POST_URL = "https://www.linkedin.com/company/135218061/admin/page-posts/published/?share=true";
const DRAFT_LIFETIME_MS = 120_000;
const draftKey = (tabId) => `draft:${tabId}`;
let queue = Promise.resolve();

async function handleMessage(message, sender) {
  if (message.type === "OPEN_POST" && !sender.tab) {
    const { posts = [], lastPosted = 0, activePost } = await chrome.storage.local.get([
      "posts", "lastPosted", "activePost"
    ]);
    if (activePost) throw new Error("یک پست در حال آماده‌سازی است. ابتدا آن را ثبت انتشار یا لغو کنید.");
    const post = Posts.nextPost(posts, lastPosted);
    if (!post) throw new Error("پست بعدی موجود نیست. پوشه را از تنظیمات انتخاب یا به‌روز کنید.");
    const text = Posts.cleanText(post.text);
    if (!text) throw new Error("متن پست بعدی پس از پاک‌سازی خالی است.");
    const tab = await chrome.tabs.create({ url: "about:blank" });
    try {
      const expiresAt = Date.now() + DRAFT_LIFETIME_MS;
      await chrome.storage.local.set({
        activePost: { number: post.number, filename: post.filename, tabId: tab.id, state: "loading", expiresAt }
      });
      await chrome.storage.session.set({
        [draftKey(tab.id)]: { text, number: post.number, expiresAt }
      });
      await chrome.tabs.update(tab.id, { url: POST_URL });
      return { ok: true };
    } catch (error) {
      await chrome.storage.session.remove(draftKey(tab.id));
      await chrome.storage.local.remove("activePost");
      throw error;
    }
  }

  if (sender.tab) {
    const url = new URL(sender.url);
    if (url.origin !== "https://www.linkedin.com" ||
        !/^\/company\/135218061\/admin\/page-posts\/published\/?$/.test(url.pathname)) {
      return { ok: false };
    }
    const key = draftKey(sender.tab.id);
    const { activePost } = await chrome.storage.local.get("activePost");
    const isActiveTab = activePost?.tabId === sender.tab.id;

    if (message.type === "GET_DRAFT") {
      const saved = await chrome.storage.session.get(key);
      const draft = saved[key];
      if (!isActiveTab || activePost.state !== "loading" || !draft || draft.expiresAt <= Date.now()) {
        await chrome.storage.session.remove(key);
        if (isActiveTab && activePost.state === "loading") await chrome.storage.local.remove("activePost");
        return { draft: null };
      }
      return { draft };
    }

    if (message.type === "GET_ACTIVE") {
      return { activePost: isActiveTab && activePost.number === message.number ? activePost : null };
    }

    if (message.type === "DRAFT_FILLED" && isActiveTab && activePost.state === "loading") {
      const countdownEndsAt = Date.now() + 5_000;
      await chrome.storage.local.set({ activePost: { ...activePost, state: "countdown", countdownEndsAt } });
      await chrome.storage.session.remove(key);
      return { ok: true, countdownEndsAt };
    }

    if (message.type === "CLAIM_PUBLISH" && isActiveTab && activePost.number === message.number) {
      if (activePost.state !== "countdown" || Date.now() < activePost.countdownEndsAt) return { ok: false };
      await chrome.storage.local.set({ activePost: { ...activePost, state: "publishing", publishStartedAt: Date.now() } });
      return { ok: true };
    }

    if (message.type === "POST_CONFIRMED" && isActiveTab &&
        activePost.number === message.number && activePost.state === "publishing") {
      await chrome.storage.local.set({ lastPosted: activePost.number, activePost: null });
      await chrome.storage.session.remove(key);
      return { ok: true };
    }

    if (message.type === "PUBLISH_FAILED" && isActiveTab && activePost.number === message.number &&
        ["countdown", "publishing"].includes(activePost.state)) {
      await chrome.storage.local.set({ activePost: { ...activePost, state: "attention", error: message.error } });
      return { ok: true };
    }

    if (message.type === "CANCEL_DRAFT" && isActiveTab && activePost.number === message.number &&
        ["loading", "countdown"].includes(activePost.state)) {
      await chrome.storage.session.remove(key);
      await chrome.storage.local.remove("activePost");
      return { ok: true };
    }

    if (message.type === "DRAFT_FAILED" && isActiveTab && activePost.state === "loading") {
      await chrome.storage.session.remove(key);
      await chrome.storage.local.remove("activePost");
      return { ok: true };
    }
    return { ok: false };
  }

  if (message.type === "MARK_PUBLISHED" || message.type === "CANCEL_DRAFT") {
    const { activePost } = await chrome.storage.local.get("activePost");
    if (!activePost || activePost.number !== message.number) {
      throw new Error("پست فعال تغییر کرده است؛ پنجره را دوباره باز کنید.");
    }
    const stalePublish = activePost.state === "publishing" && Date.now() - activePost.publishStartedAt > 60_000;
    if (message.type === "MARK_PUBLISHED") {
      if (!["ready", "attention"].includes(activePost.state) && !stalePublish) throw new Error("ابتدا منتظر پایان عملیات انتشار بمانید.");
      await chrome.storage.local.set({ lastPosted: activePost.number });
    } else if (activePost.state === "publishing" && !stalePublish) {
      throw new Error("درخواست انتشار ارسال شده است؛ تا مشخص شدن نتیجه صبر کنید.");
    }
    await chrome.storage.session.remove(draftKey(activePost.tabId));
    await chrome.storage.local.remove("activePost");
    return { ok: true };
  }
  return { ok: false };
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  // Serialize requests so two fast clicks cannot reserve the same post twice.
  const result = queue.then(() => handleMessage(message, sender));
  queue = result.catch(() => {});
  result.then(sendResponse, (error) => {
    console.error("LinkedIn draft:", error);
    sendResponse({ ok: false, error: error.message });
  });
  return true;
});

chrome.tabs.onRemoved.addListener((tabId) => {
  queue = queue.then(async () => {
    await chrome.storage.session.remove(draftKey(tabId));
    const { activePost } = await chrome.storage.local.get("activePost");
    if (activePost?.tabId === tabId && activePost.state === "loading") {
      await chrome.storage.local.remove("activePost");
    } else if (activePost?.tabId === tabId && ["countdown", "publishing"].includes(activePost.state)) {
      await chrome.storage.local.set({ activePost: { ...activePost, state: "attention", error: "تب پیش از تأیید انتشار بسته شد." } });
    }
    // A filled draft remains available for manual confirmation after closing its tab.
  }).catch(console.error);
});
