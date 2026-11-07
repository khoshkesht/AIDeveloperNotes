const status = document.getElementById("status");
const counter = document.getElementById("last-posted");
let directoryHandle;
let busy = false;

function openDatabase() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open("linkedin-post-folder", 1);
    request.onupgradeneeded = () => request.result.createObjectStore("settings");
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function storedHandle(handle) {
  const db = await openDatabase();
  try {
    return await new Promise((resolve, reject) => {
      const transaction = db.transaction("settings", handle ? "readwrite" : "readonly");
      const store = transaction.objectStore("settings");
      const request = handle ? store.put(handle, "directory") : store.get("directory");
      transaction.oncomplete = () => resolve(handle || request.result);
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error || new Error("ذخیرهٔ پوشه انجام نشد."));
    });
  } finally { db.close(); }
}

async function render(updateCounter = false) {
  const { posts = [], lastPosted = 0, folderName, importedAt } = await chrome.storage.local.get([
    "posts", "lastPosted", "folderName", "importedAt"
  ]);
  if (updateCounter) counter.value = lastPosted;
  document.getElementById("folder-summary").textContent = folderName
    ? `پوشه: ${folderName} | ${posts.length} پست | آخرین خواندن: ${new Date(importedAt).toLocaleString("fa-IR")}`
    : "هنوز پوشه‌ای انتخاب نشده است.";
  const next = Posts.nextPost(posts, lastPosted);
  document.getElementById("next-title").textContent = next ? `پیش‌نمایش پست ${next.number} (${next.filename})` : "پست بعدی موجود نیست";
  document.getElementById("preview").textContent = next ? Posts.cleanText(next.text) : "پوشه را انتخاب یا فایل‌ها را به‌روز کنید؛ شمارهٔ آخرین انتشار را هم بررسی کنید.";
}

async function importFolder(handle) {
  const posts = [];
  const numbers = new Set();
  let ignored = 0;
  for await (const [filename, entry] of handle.entries()) {
    const number = Posts.postNumber(filename);
    if (entry.kind !== "file" || number === null) { ignored++; continue; }
    if (numbers.has(number)) throw new Error(`دو فایل با شمارهٔ ${number} وجود دارد. شماره‌ها باید یکتا باشند.`);
    const file = await entry.getFile();
    const text = await file.text();
    if (!Posts.cleanText(text)) throw new Error(`فایل ${filename} متن قابل ارسال ندارد.`);
    const header = /^\uFEFF?\s*#Post_(\d+)\b/i.exec(text);
    if (header && Number(header[1]) !== number) {
      throw new Error(`شمارهٔ داخل ${filename} با نام فایل یکسان نیست.`);
    }
    numbers.add(number);
    posts.push({ number, filename, text });
  }
  if (!posts.length) throw new Error("فایل شماره‌دار txt یا md در این پوشه پیدا نشد؛ پوشهٔ قبلی حفظ شد.");
  posts.sort((a, b) => a.number - b.number);
  await storedHandle(handle);
  await chrome.storage.local.set({ posts, folderName: handle.name, importedAt: Date.now() });
  directoryHandle = handle;
  await render();
  status.textContent = `${posts.length} پست خوانده شد؛ ${ignored} مورد با نام نامرتبط یا پوشه نادیده گرفته شد. شمارهٔ آخرین انتشار حفظ شد.`;
}

async function folderAction(choose) {
  if (busy) return;
  busy = true;
  document.querySelectorAll(".actions button").forEach(button => { button.disabled = true; });
  try {
    let handle = directoryHandle;
    if (choose || !handle) {
      handle = await window.showDirectoryPicker({ id: "linkedin-posts", mode: "read" });
    } else if (await handle.requestPermission({ mode: "read" }) !== "granted") {
      throw new Error("اجازهٔ خواندن پوشه داده نشد. دوباره پوشه را انتخاب کنید.");
    }
    status.textContent = "در حال خواندن فایل‌ها…";
    await importFolder(handle);
  } catch (error) {
    status.textContent = error.name === "AbortError" ? "انتخاب پوشه لغو شد." : error.message;
  } finally {
    busy = false;
    document.querySelectorAll(".actions button").forEach(button => { button.disabled = false; });
  }
}

document.getElementById("choose-folder").addEventListener("click", () => folderAction(true));
document.getElementById("refresh-folder").addEventListener("click", () => folderAction(false));
document.getElementById("counter-form").addEventListener("submit", async event => {
  event.preventDefault();
  const number = Number(counter.value);
  if (counter.value.trim() === "" || !Number.isSafeInteger(number) || number < 0) {
    status.textContent = "یک عدد صحیح صفر یا بیشتر وارد کنید.";
    return;
  }
  try {
    await chrome.storage.local.set({ lastPosted: number });
    await render();
    status.textContent = "شمارهٔ آخرین انتشار ذخیره شد.";
  } catch (error) { status.textContent = error.message; }
});

async function initialize() {
  await render(true);
  directoryHandle = await storedHandle();
}
initialize().catch(error => { status.textContent = error.message; });
