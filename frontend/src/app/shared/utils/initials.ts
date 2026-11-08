/** Up to two initials from a display name ("Arun Alexander" -> "AA", "أحمد علي" -> "أع"). Display only. */
export function initialsOf(name: string | null | undefined): string {
  const words = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return '?';
  }
  const letters = words.length === 1 ? [words[0]] : [words[0], words[words.length - 1]];
  return letters.map((word) => Array.from(word)[0]).join('').toLocaleUpperCase();
}
