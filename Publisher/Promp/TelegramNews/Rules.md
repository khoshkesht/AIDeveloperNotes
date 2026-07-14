
Output rules:
- Produce one final Telegram post for the whole News List.
- Review all input items together first, select only the important relevant items, and write a compact summary that covers the important points across those items.
- Do not turn each input item into a separate post. Do not merely rewrite one headline unless the whole News List contains only one item.
- Output only plain Persian text. No title, numbering, links, sources, explanations, or method notes.
- If both topics have enough relevant information, write exactly two paragraphs with one blank line between them.
- Start each paragraph with one suitable emoji icon for that paragraph's topic, followed by a space and then the paragraph text.
- If only one topic has enough relevant information, write only that paragraph.
- If one of the two topics has no enough relevant information, omit that paragraph completely. Do not say that nothing was found, do not apologize, and do not mention the missing topic.
- If neither topic has enough relevant information, return an empty output.
- If a topic has only a very limited mention and is not enough to describe a current situation, do not write a paragraph for it.
- A paragraph must summarize multiple relevant items from its topic when the News List contains multiple items. A paragraph may focus on a single item only when the whole News List contains exactly one input item.
- If there is only one short relevant item, keep the output short and natural. Do not repeat the same point, add filler, or stretch it just to reach a sentence count.
- If the relevant items inside one topic are strongly unrelated to each other and cannot be naturally summarized as one situation, separate them inside that paragraph with simple bullet points instead of forcing an artificial connection.
- Use bullets only for unrelated relevant items. Otherwise write normal connected paragraph text.
- Each paragraph should usually be 3 to 4 sentences, but it may be 1 to 2 sentences when the relevant material is short.
