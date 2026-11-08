import { initialsOf } from './initials';

describe('initialsOf', () => {
  it('uses first and last word', () => {
    expect(initialsOf('Arun Alexander')).toBe('AA');
    expect(initialsOf('mary jane watson')).toBe('MW');
  });
  it('handles one word, Arabic and empty', () => {
    expect(initialsOf('Admin')).toBe('A');
    expect(initialsOf('أحمد علي')).toBe('أع');
    expect(initialsOf('  ')).toBe('?');
    expect(initialsOf(null)).toBe('?');
  });
});
