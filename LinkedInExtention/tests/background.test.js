const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const Posts = require("../posts.js");
const POST_URL = "https://www.linkedin.com/company/135218061/admin/page-posts/published/?share=true";

function setup() {
  const local = { lastPosted: 9, posts: [
    { number: 11, filename: "Post_011.txt", text: "#Post_11\nبعدی" },
    { number: 10, filename: "Post_010.txt", text: "#Post_10\n<b>عنوان</b>\n@AIDeveloperNotes" }
  ] };
  const session = {};
  const calls = [];
  let listener;
  let removed;
  let nextTab = 42;
  let now = Date.now();
  const store = data => ({
    get: async keys => Object.fromEntries((Array.isArray(keys) ? keys : [keys]).map(key => [key, structuredClone(data[key])])),
    set: async values => Object.assign(data, structuredClone(values)),
    remove: async key => { delete data[key]; }
  });
  const chrome = {
    storage: { local: store(local), session: store(session) },
    runtime: { onMessage: { addListener: fn => { listener = fn; } } },
    tabs: {
      create: async options => { calls.push(["create", options]); return { id: nextTab++ }; },
      update: async (id, options) => { assert.ok(session[`draft:${id}`]); calls.push(["update", id, options]); },
      onRemoved: { addListener: fn => { removed = fn; } }
    }
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../background.js"), "utf8"), {
    chrome, Posts, importScripts() {}, URL, Date: { now: () => now }, console: { error() {} }
  });
  const send = (type, sender = {}, extra = {}) => new Promise(resolve => listener({ type, ...extra }, sender, resolve));
  const sender = id => ({ tab: { id }, url: POST_URL });
  return { local, session, calls, send, sender, removed: id => removed(id), advance: ms => { now += ms; } };
}

test("waits five seconds and saves the counter only after publication confirmation", async () => {
  const app = setup();
  assert.equal((await app.send("OPEN_POST")).ok, true);
  assert.equal(app.local.lastPosted, 9);
  assert.equal(app.local.activePost.number, 10);
  assert.equal(app.calls[1][2].url, POST_URL);
  const draft = (await app.send("GET_DRAFT", app.sender(42))).draft;
  assert.equal(draft.text, "عنوان\n@AIDeveloperNotes");
  assert.equal((await app.send("MARK_PUBLISHED", {}, { number: 10 })).ok, false);
  await app.send("DRAFT_FILLED", app.sender(42));
  assert.equal(app.local.lastPosted, 9);
  assert.equal(app.local.activePost.state, "countdown");
  assert.equal((await app.send("MARK_PUBLISHED", app.sender(42), { number: 10 })).ok, false);
  assert.equal((await app.send("DRAFT_FILLED", app.sender(99))).ok, false);
  assert.equal((await app.send("POST_CONFIRMED", app.sender(42), { number: 10 })).ok, false);
  assert.equal((await app.send("CLAIM_PUBLISH", app.sender(42), { number: 10 })).ok, false);
  app.advance(5_000);
  assert.equal((await app.send("CLAIM_PUBLISH", app.sender(42), { number: 10 })).ok, true);
  assert.equal(app.local.lastPosted, 9);
  assert.equal((await app.send("CLAIM_PUBLISH", app.sender(42), { number: 10 })).ok, false);
  assert.equal((await app.send("POST_CONFIRMED", app.sender(99), { number: 10 })).ok, false);
  await app.send("POST_CONFIRMED", app.sender(42), { number: 10 });
  assert.equal(app.local.lastPosted, 10);
  assert.equal(app.local.activePost, null);
  await app.send("OPEN_POST");
  assert.equal(app.local.activePost.number, 11);
});

test("double clicks reserve only one tab; cancellation and failure preserve the counter", async () => {
  const app = setup();
  const results = await Promise.all([app.send("OPEN_POST"), app.send("OPEN_POST")]);
  assert.equal(results.filter(result => result.ok).length, 1);
  assert.equal(app.calls.filter(call => call[0] === "create").length, 1);
  await app.send("CANCEL_DRAFT", {}, { number: 10 });
  assert.equal(app.local.lastPosted, 9);
  assert.equal((await app.send("GET_DRAFT", app.sender(42))).draft, null);
  await app.send("OPEN_POST");
  await app.send("DRAFT_FAILED", app.sender(43));
  assert.equal(app.local.lastPosted, 9);
  assert.equal(app.local.activePost, undefined);
});

test("expired drafts do not advance the counter; filled drafts survive tab closure", async () => {
  const app = setup();
  await app.send("OPEN_POST");
  app.session["draft:42"].expiresAt = 0;
  assert.equal((await app.send("GET_DRAFT", app.sender(42))).draft, null);
  assert.equal(app.local.activePost, undefined);
  assert.equal(app.local.lastPosted, 9);
  await app.send("OPEN_POST");
  await app.send("DRAFT_FILLED", app.sender(43));
  app.removed(43);
  // The next message runs behind onRemoved in the service worker queue.
  assert.equal((await app.send("OPEN_POST")).ok, false);
  assert.equal(app.local.activePost.state, "attention");
  await app.send("MARK_PUBLISHED", {}, { number: 10 });
  assert.equal(app.local.lastPosted, 10);
});

test("cancel during countdown prevents publishing and never advances the counter", async () => {
  const app = setup();
  await app.send("OPEN_POST");
  await app.send("DRAFT_FILLED", app.sender(42));
  await app.send("CANCEL_DRAFT", app.sender(42), { number: 10 });
  app.advance(5_000);
  assert.equal((await app.send("CLAIM_PUBLISH", app.sender(42), { number: 10 })).ok, false);
  assert.equal(app.local.lastPosted, 9);
});

test("uncertain publication preserves the counter and permits manual recovery", async () => {
  const app = setup();
  await app.send("OPEN_POST");
  await app.send("DRAFT_FILLED", app.sender(42));
  app.advance(5_000);
  await app.send("CLAIM_PUBLISH", app.sender(42), { number: 10 });
  assert.equal((await app.send("CANCEL_DRAFT", {}, { number: 10 })).ok, false);
  await app.send("PUBLISH_FAILED", app.sender(42), { number: 10, error: "No success message" });
  assert.equal(app.local.lastPosted, 9);
  assert.equal(app.local.activePost.state, "attention");
  await app.send("MARK_PUBLISHED", {}, { number: 10 });
  assert.equal(app.local.lastPosted, 10);
});
