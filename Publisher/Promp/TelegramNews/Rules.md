
Output rules:
- Produce one final Telegram post for the whole News List.
- Output only plain Persian text. No title, numbering, links, sources, explanations, or method notes.
- If both topics have enough relevant information, write exactly two paragraphs with one blank line between them.
- Start each paragraph with one suitable emoji icon for that paragraph's topic, followed by a space and then the paragraph text.
- If only one topic has enough relevant information, write only that paragraph.
- If one of the two topics has no enough relevant information, omit that paragraph completely. Do not say that nothing was found, do not apologize, and do not mention the missing topic.
- If neither topic has enough relevant information, return an empty output.
- If a topic has only a very limited mention and is not enough to describe a current situation, do not write a paragraph for it.
- If the relevant items inside one topic are strongly unrelated to each other and cannot be naturally summarized as one situation, separate them inside that paragraph with simple bullet points instead of forcing an artificial connection.
- Use bullets only for unrelated relevant items. Otherwise write normal connected paragraph text.
- Each paragraph must be 4 to 6 sentences.
