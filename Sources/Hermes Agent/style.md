# Hermes Agent Post Image Style

Create one standalone image for one post only.

## Output

- Canvas: exactly 1080 x 1350 px.
- Aspect ratio: exactly 4:5 vertical.
- File type: PNG.
- One image = one post = one output file.
- No collage, storyboard, contact sheet, multi-post grid, or multiple scenes.

## Fixed Layout

- Use a strict two-zone layout.
- Top zone: exactly the upper 50% of the canvas, from y=0 to y=675.
- Bottom zone: exactly the lower 50% of the canvas, from y=675 to y=1350.
- Keep a clear horizontal separation between the text zone and illustration zone.
- The illustration must not enter the top text zone.
- The main text block must not enter the bottom illustration zone.
- Footer sits inside the bottom zone at the very bottom, small and unobtrusive.

## Top Zone: Text

- Put the post title and body text only in the top 50%.
- Persian text must be RTL, right-aligned, connected correctly, and readable.
- Use a clean modern sans-serif style.
- Title: bold, clear, near the top-right.
- Body: smaller than title, comfortable line height.
- If the full body is too long for the top 50%, use a concise faithful summary instead of shrinking the text too much.
- Do not place diagrams, icons, or decorative elements behind the text.

## Bottom Zone: Illustration

- Use the bottom 50% for a single conceptual isometric illustration.
- The illustration must directly represent the post topic.
- Use clean technical objects such as app windows, terminal panels, nodes, connectors, servers, model providers, memory blocks, tools, workflows, or schedulers only when relevant.
- Keep the composition simple, focused, and readable.
- Use thin directional connectors and subtle shadows.
- Do not reuse the exact same illustration concept for different posts.

## Visual Style

- Style: modern isometric technical illustration.
- Mood: professional, clean, educational, suitable for AI and software engineering content.
- Background: light neutral, warm white or very light gray.
- Palette: warm white, light gray, pale cyan, turquoise, soft violet, controlled green.
- Avoid dark blue as the dominant theme.
- Avoid neon colors, heavy contrast, noisy backgrounds, cartoonish characters, fantasy styling, and marketing-banner aesthetics.
- Use soft studio lighting and consistent subtle shadows.

## Footer

- At the bottom, include only:
  - a small Telegram icon
  - `@AIDeveloperNotes`
- Do not add words such as source, channel, Telegram Channel, or any extra footer text.
- The footer must stay small and consistent across all images.

## Must Avoid

- Wrong aspect ratio.
- Any canvas other than 1080 x 1350 px.
- Text zone larger than 50%.
- Illustration zone smaller than 50%.
- Illustration overlapping the text.
- Tiny unreadable Persian text.
- Broken, mirrored, or left-aligned Persian text.
- Dark blue dominant background.
- Extra logos, watermarks, slogans, or unrelated elements.

## Generation Checklist

- Exact 1080 x 1350 px vertical PNG.
- Top half contains only the title and body text.
- Bottom half contains only one post-specific isometric illustration plus the small footer.
- Persian text is RTL, right-aligned, connected, and readable.
- The image represents only one post.
- The footer contains only the Telegram icon and `@AIDeveloperNotes`.
