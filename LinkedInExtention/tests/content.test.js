const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

async function page({ success = true, insertDelay = 0, commandResult = true, renderedText = "متن پست", dropInsertion = false,
  removeWrapper = false, replaceEditor = false, textContentOnly = false } = {}) {
  let now = 0;
  let state = "loading";
  let clicks = 0;
  let confirmed = false;
  let insertions = 0;
  const timers = [];
  const elements = [];
  const messages = [];
  const toasts = [];
  const draft = { number: 10, text: "متن پست", expiresAt: 120_000 };
  const makeElement = tag => {
    const element = {
      tag, style: {}, children: [], events: {}, textContent: "", hidden: false,
      append(...children) { this.children.push(...children); },
      setAttribute() {}, remove() {}, getClientRects: () => [1],
      addEventListener(type, handler) { this.events[type] = handler; }
    };
    elements.push(element);
    return element;
  };
  const button = {
    getClientRects: () => [1], disabled: false, getAttribute: () => null,
    click() {
      clicks++;
      if (success) toasts.push({ textContent: "Your post has been published.", getClientRects: () => [1] });
    }
  };
  let currentEditor;
  const dialog = { isConnected: true, getClientRects: () => [1], querySelectorAll: selector =>
    selector.includes("contenteditable") ? [currentEditor] : [button] };
  const editor = { innerText: "", isConnected: true, isContentEditable: true, contains: () => false,
    getClientRects: () => [1], focus() {}, dispatchEvent() {}, closest: () => dialog };
  currentEditor = editor;
  let wrapperExists = true;
  const container = { matches: () => false, querySelectorAll: () => [currentEditor] };
  const chrome = { runtime: { sendMessage: async message => {
    messages.push(message);
    switch (message.type) {
      case "GET_DRAFT": return { draft: state === "loading" ? draft : null };
      case "DRAFT_FILLED": state = "countdown"; return { ok: true, countdownEndsAt: now + 5_000 };
      case "GET_ACTIVE": return { activePost: state ? { state } : null };
      case "CLAIM_PUBLISH": {
        if (state !== "countdown") return { ok: false };
        state = "publishing";
        return { ok: true };
      }
      case "POST_CONFIRMED": confirmed = true; state = null; return { ok: true };
      case "CANCEL_DRAFT": state = null; return { ok: true };
      case "PUBLISH_FAILED": state = "attention"; return { ok: true };
      default: return { ok: true };
    }
  } } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../content.js"), "utf8"), {
    chrome, console, Date: { now: () => now },
    getComputedStyle: () => ({ visibility: "visible" }),
    setTimeout: (fn, ms) => { timers.push({ fn, at: now + ms }); },
    Event: class {},
    window: { getSelection: () => ({ removeAllRanges() {}, addRange() {} }) },
    document: {
      body: makeElement("body"), createElement: makeElement,
      createRange: () => ({ selectNodeContents() {} }),
      querySelectorAll: selector => selector === ".editor-content.ql-container" ? (wrapperExists ? [container] : [])
        : selector.includes('role="dialog"') ? [dialog] : toasts,
      execCommand: () => {
        insertions++;
        if (!dropInsertion) {
          const render = () => {
            if (removeWrapper) wrapperExists = false;
            if (replaceEditor) {
              editor.isConnected = false;
              currentEditor = { ...editor, isConnected: true };
            }
            if (textContentOnly) currentEditor.textContent = renderedText;
            else currentEditor.innerText = renderedText;
          };
          if (insertDelay) timers.push({ fn: render, at: now + insertDelay });
          else render();
        }
        return commandResult;
      }
    }
  });
  const flush = () => new Promise(resolve => setImmediate(resolve));
  await flush();
  const advance = async ms => {
    const target = now + ms;
    for (;;) {
      timers.sort((a, b) => a.at - b.at);
      if (!timers.length || timers[0].at > target) break;
      const next = timers.shift();
      now = next.at;
      next.fn();
      await flush();
    }
    now = target;
    await flush();
  };
  return { advance, editor, messages, elements,
    clicks: () => clicks, confirmed: () => confirmed, insertions: () => insertions,
    cancel: async () => {
      const cancel = elements.find(element => element.tag === "button");
      await cancel.events.click();
    }
  };
}

test("fills text, counts down five seconds, clicks once and records confirmed success", async () => {
  const app = await page();
  assert.equal(app.editor.innerText, "متن پست");
  await app.advance(500);
  assert.ok(app.elements.some(element => element.textContent.includes("5 ثانیه")));
  await app.advance(4999);
  assert.equal(app.clicks(), 0);
  await app.advance(1);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
  await app.advance(60_000);
  assert.equal(app.clicks(), 1);
});

test("countdown cancellation prevents any click or confirmation", async () => {
  const app = await page();
  await app.advance(5000);
  await app.cancel();
  await app.advance(60_000);
  assert.equal(app.clicks(), 0);
  assert.equal(app.confirmed(), false);
});

test("a missing success message never records success and does not retry", async () => {
  const app = await page({ success: false });
  await app.advance(60_000);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), false);
  assert.ok(app.messages.some(message => message.type === "PUBLISH_FAILED"));
});

test("a different nonempty text is accepted without comparing to the file", async () => {
  const app = await page();
  await app.advance(500);
  app.editor.innerText = "متن دیگری";
  await app.advance(5_000);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
});

test("clearing the editor during countdown prevents publication", async () => {
  const app = await page();
  await app.advance(500);
  app.editor.innerText = "\n \u200B";
  await app.advance(5_000);
  assert.equal(app.clicks(), 0);
  assert.equal(app.confirmed(), false);
});

test("waits for delayed rendering even if execCommand returns false, then starts the full countdown", async () => {
  const app = await page({ insertDelay: 2000, commandResult: false });
  await app.advance(1999);
  assert.ok(!app.messages.some(message => message.type === "DRAFT_FILLED"));
  await app.advance(501);
  assert.ok(app.messages.some(message => message.type === "DRAFT_FILLED"));
  assert.equal(app.insertions(), 1);
  await app.advance(4999);
  assert.equal(app.clicks(), 0);
  await app.advance(1);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
});

test("ignores harmless rendered whitespace differences during insertion and publication checks", async () => {
  const app = await page({ renderedText: "\nمتن\u00a0\nپست\n\n" });
  await app.advance(5_500);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
});

test("times out an actual insertion failure without re-inserting or starting publication", async () => {
  const app = await page({ dropInsertion: true });
  await app.advance(20_000);
  assert.equal(app.insertions(), 1);
  assert.equal(app.clicks(), 0);
  assert.equal(app.confirmed(), false);
  assert.ok(!app.messages.some(message => message.type === "DRAFT_FILLED"));
  assert.ok(app.messages.some(message => message.type === "DRAFT_FAILED"));
});

test("keeps the filled editor when Quill removes its original wrapper classes", async () => {
  const app = await page({ removeWrapper: true });
  await app.advance(5_500);
  assert.equal(app.insertions(), 1);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
});

test("finds a replacement editor inside the same dialog after a rerender", async () => {
  const app = await page({ removeWrapper: true, replaceEditor: true });
  await app.advance(5_500);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
});

test("accepts textContent when the editor's innerText is empty", async () => {
  const app = await page({ textContentOnly: true });
  await app.advance(5_500);
  assert.equal(app.clicks(), 1);
  assert.equal(app.confirmed(), true);
});
