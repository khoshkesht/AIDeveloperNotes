(() => {
  const visible = element => element && element.getClientRects().length && getComputedStyle(element).visibility !== "hidden";
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  const send = (type, number, extra = {}) => chrome.runtime.sendMessage({ type, number, ...extra });

  function panel() {
    const element = document.createElement("div");
    element.dir = "rtl";
    element.setAttribute("role", "status");
    Object.assign(element.style, {
      position: "fixed", bottom: "24px", right: "24px", zIndex: "2147483647",
      maxWidth: "380px", padding: "16px", borderRadius: "10px", background: "#123b65",
      color: "white", boxShadow: "0 4px 20px #0003", font: "14px/1.9 Tahoma, sans-serif"
    });
    const label = document.createElement("div");
    element.append(label);
    document.body.append(element);
    return { element, label };
  }

  function notify(text) {
    const { element, label } = panel();
    label.textContent = text;
    setTimeout(() => element.remove(), 15000);
  }

  const dialogSelector = '[role="dialog"], .share-box-v2__modal, .share-box__modal';
  const editorScopes = new WeakMap();
  const usable = editor => editor && editor.isConnected !== false && visible(editor) && editor.isContentEditable !== false;
  const rememberEditor = editor => {
    const dialog = editor?.closest(dialogSelector);
    if (dialog) editorScopes.set(editor, dialog);
    return editor;
  };

  function findEditor(reference = null) {
    // Quill can change its classes/attributes after insertion. Keep using the
    // actual node we filled instead of requiring the original selector again.
    const scope = reference && editorScopes.get(reference);
    if (scope && (scope.isConnected === false || !visible(scope))) return null;
    if (usable(reference) && hasEditorText(reference)) return reference;
    const candidates = [];
    const editableSelector = '[contenteditable="true"], [contenteditable=""], [contenteditable="plaintext-only"]';
    if (scope) {
      candidates.push(...scope.querySelectorAll(editableSelector));
    } else for (const container of document.querySelectorAll(".editor-content.ql-container")) {
      // Prefer the actual Quill text area over its surrounding container.
      let editors = [...container.querySelectorAll('.ql-editor[contenteditable="true"]')];
      if (!editors.length) editors = [...container.querySelectorAll('[contenteditable="true"]')];
      if (!editors.length && container.matches('[contenteditable="true"]')) editors = [container];
      candidates.push(...editors.filter(visible));
    }
    // A rerender can remove the original .editor-content.ql-container wrapper.
    if (!scope && !candidates.length) {
      for (const dialog of document.querySelectorAll(dialogSelector)) {
        if (visible(dialog)) candidates.push(...dialog.querySelectorAll(editableSelector));
      }
    }
    const unique = [...new Set(candidates)].filter(usable);
    if (scope) {
      const filled = unique.filter(hasEditorText);
      if (filled.length === 1) return rememberEditor(filled[0]);
    }
    if (unique.includes(reference)) return reference;
    const focused = unique.filter(editor => editor === document.activeElement || editor.contains(document.activeElement));
    if (focused.length === 1) return rememberEditor(focused[0]);
    return unique.length === 1 ? rememberEditor(unique[0]) : null;
  }

  function hasEditorText(editor) {
    return Boolean(editor && [editor.innerText, editor.textContent].some(text =>
      (text || "").replace(/[\s\u061C\u200B-\u200F\u202A-\u202E\u2066-\u2069\uFE0E\uFE0F\uFEFF]/g, "").length > 0
    ));
  }

  async function waitForInsertedText(reference) {
    const deadline = Date.now() + 5000;
    let matchingSince = null;
    while (Date.now() <= deadline) {
      // The editor element itself can be replaced while LinkedIn renders it.
      const editor = findEditor(reference);
      if (hasEditorText(editor)) {
        if (matchingSince === null) matchingSince = Date.now();
        if (Date.now() - matchingSince >= 500) return editor;
      } else {
        matchingSince = null;
      }
      await pause(250);
    }
    throw new Error("ویرایشگر پس از ۵ ثانیه هنوز خالی یا در دسترس نیست؛ ارسال انجام نشد.");
  }

  function postButton(dialog) {
    if (!dialog) return null;
    const buttons = [...dialog.querySelectorAll('button.share-actions__primary-action, button[data-test-share-actions-primary-action]')];
    if (!buttons.length) {
      buttons.push(...[...dialog.querySelectorAll("button")].filter(button =>
        /^(post|publish|پست|انتشار|ارسال پست)$/i.test((button.innerText || button.getAttribute("aria-label") || "").trim())
      ));
    }
    const candidates = buttons.filter(button => visible(button) && !button.disabled && button.getAttribute("aria-disabled") !== "true");
    return candidates.length === 1 ? candidates[0] : null;
  }

  const toastSelector = '.artdeco-toast-item, [data-test-artdeco-toast-item], [role="alert"]';
  const successText = /(?:post successful|post (?:was |has been )?(?:successfully )?(?:published|shared)|(?:successfully (?:posted|published|shared))|your post is (?:now )?live|پست.*(?:منتشر شد|با موفقیت منتشر)|با موفقیت.*منتشر)/i;

  async function publish(editor, draft, countdownEndsAt) {
    const { element, label } = panel();
    const cancel = document.createElement("button");
    cancel.textContent = "لغو ارسال";
    Object.assign(cancel.style, {
      marginTop: "10px", padding: "8px 16px", cursor: "pointer", font: "inherit",
      background: "#dc2626", color: "#ffffff", border: "1px solid #fca5a5", borderRadius: "8px"
    });
    element.append(cancel);
    let cancelled = false;
    let claimed = false;
    cancel.addEventListener("click", async () => {
      cancelled = true;
      cancel.disabled = true;
      label.textContent = "ارسال خودکار لغو شد. متن در ویرایشگر باقی مانده است.";
      await send("CANCEL_DRAFT", draft.number).catch(console.error);
    });

    try {
      while (!cancelled) {
        const current = await send("GET_ACTIVE", draft.number);
        if (current?.activePost?.state !== "countdown") {
          cancelled = true;
          label.textContent = "شمارش معکوس متوقف شد.";
          break;
        }
        const remaining = Math.max(0, Math.ceil((countdownEndsAt - Date.now()) / 1000));
        label.textContent = `انتشار پست ${draft.number} تا ${remaining} ثانیهٔ دیگر…`;
        if (remaining === 0) break;
        await pause(250);
      }
      if (cancelled) return;
      editor = findEditor(editor);
      const dialog = editor && (editorScopes.get(editor) || editor.closest(dialogSelector));
      const button = postButton(dialog);
      if (!visible(editor) || !hasEditorText(editor) || !button) {
        throw new Error("ویرایشگر خالی یا بسته است یا دکمهٔ پست آماده نیست؛ انتشار انجام نشد.");
      }
      const before = new Map([...document.querySelectorAll(toastSelector)].map(toast => [toast, toast.textContent]));
      // Claim once in the worker before clicking; reloads cannot submit this draft again.
      cancel.disabled = true;
      const claim = await send("CLAIM_PUBLISH", draft.number);
      if (!claim?.ok || cancelled) return;
      claimed = true;
      cancel.hidden = true;
      label.textContent = "در حال ارسال؛ منتظر تأیید لینکدین…";
      button.click();

      const timeout = Date.now() + 45_000;
      while (Date.now() < timeout) {
        const success = [...document.querySelectorAll(toastSelector)].some(toast =>
          visible(toast) && before.get(toast) !== toast.textContent && successText.test(toast.textContent)
        );
        // Closing the editor alone does not prove that LinkedIn accepted the post.
        if (success) {
          const result = await send("POST_CONFIRMED", draft.number);
          if (!result?.ok) throw new Error("انتشار تأیید شد اما ذخیرهٔ شماره انجام نشد؛ شماره را در تنظیمات بررسی کنید.");
          label.textContent = `پست ${draft.number} منتشر و شمارهٔ آن ذخیره شد.`;
          return;
        }
        await pause(300);
      }
      throw new Error("تأیید انتشار دریافت نشد؛ شماره ذخیره نشد. نتیجه را در لینکدین بررسی و در صورت انتشار، از منوی افزونه ثبت کنید. ارسال خودکار تکرار نمی‌شود.");
    } catch (error) {
      label.textContent = error.message;
      await send("PUBLISH_FAILED", draft.number, { error: error.message, clicked: claimed }).catch(console.error);
    } finally {
      cancel.hidden = true;
      setTimeout(() => element.remove(), 20000);
    }
  }

  async function fillDraft() {
    const response = await send("GET_DRAFT");
    const draft = response?.draft;
    if (!draft) return;
    while (Date.now() < draft.expiresAt) {
      const editor = findEditor();
      if (!editor) { await pause(500); continue; }
      const current = await send("GET_DRAFT");
      if (current?.draft?.number !== draft.number) return;
      if (!hasEditorText(editor)) {
        editor.focus();
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(editor);
        selection.removeAllRanges();
        selection.addRange(range);
        // The command's return value is not proof of Quill's final rendered text.
        // Insert once, then wait instead of retrying and potentially duplicating it.
        document.execCommand("insertText", false, draft.text);
        editor.dispatchEvent(new Event("change", { bubbles: true }));
      }
      const settledEditor = await waitForInsertedText(editor);
      const ready = await send("DRAFT_FILLED", draft.number);
      if (ready?.ok) await publish(settledEditor, draft, ready.countdownEndsAt);
      return;
    }
    throw new Error("ویرایشگر پست پیدا نشد. دسترسی مدیریت صفحه را بررسی کنید.");
  }

  fillDraft().catch(async error => {
    console.error("LinkedIn post:", error);
    await send("DRAFT_FAILED").catch(console.error);
    notify(error.message);
  });
})();
