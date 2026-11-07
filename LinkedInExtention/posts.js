/* Shared by the settings page, service worker and Node validation. */
(function (root) {
  function postNumber(filename) {
    const match = /^(?:post[_ -]?)?(\d+)\.(?:txt|md)$/i.exec(filename);
    if (!match) return null;
    const number = Number(match[1]);
    return Number.isSafeInteger(number) && number > 0 ? number : null;
  }

  function cleanText(text) {
    return text
      .replace(/^\uFEFF/, "")
      .replace(/\r\n?/g, "\n")
      .replace(/^\s*#Post_\d+[^\S\n]*(?:\n|$)/i, "")
      .replace(/<\/?b\s*>/gi, "")
      .replace(/➡\uFE0F?\s*@AIDeveloperNotes\b/g, "➤ @AIDeveloperNotes")
      .trim();
  }

  function nextPost(posts, lastPosted) {
    return posts.filter(post => post.number > lastPosted)
      .sort((a, b) => a.number - b.number)[0] || null;
  }

  const api = { postNumber, cleanText, nextPost };
  root.Posts = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(globalThis);
