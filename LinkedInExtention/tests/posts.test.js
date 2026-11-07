const { test } = require("node:test");
const assert = require("node:assert/strict");
const { cleanText, postNumber, nextPost } = require("../posts.js");

test("cleans the header, bold tags and all channel mentions while preserving paragraphs", () => {
  assert.equal(cleanText("\uFEFF#Post_10\r\n\r\n<b>عنوان</b>\r\n\r\nمتن\r\n➡️@AIDeveloperNotes\r\n@AIDeveloperNotes\r\n#tag\r\n"),
    "عنوان\n\nمتن\n➤ @AIDeveloperNotes\n@AIDeveloperNotes\n#tag");
  assert.equal(cleanText("#Post_10\n<b></b>"), "");
  assert.equal(cleanText("متن\n#Post_10"), "متن\n#Post_10");
});

test("recognizes numbered text files and skips unrelated files", () => {
  assert.equal(postNumber("Post_001.txt"), 1);
  assert.equal(postNumber("Post_10.md"), 10);
  assert.equal(postNumber("11.txt"), 11);
  for (const name of ["added.txt", "Post_0.txt", "Post_1.png", "Post_9007199254740992.txt"]) {
    assert.equal(postNumber(name), null);
  }
});

test("selects the next numeric post, including gaps and a manually reset counter", () => {
  const posts = [{ number: 10 }, { number: 2 }, { number: 1 }];
  assert.equal(nextPost(posts, 0).number, 1);
  assert.equal(nextPost(posts, 2).number, 10);
  assert.equal(nextPost(posts, 10), null);
  assert.equal(nextPost(posts, 1).number, 2);
  assert.deepEqual(posts.map(p => p.number), [10, 2, 1]);
});
