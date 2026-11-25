import { describe, expect, it } from 'vitest';
import { returnPathFrom } from './returnPath';

const from = (pathname: string, search = '', hash = '') => ({ from: { pathname, search, hash } });

describe('returnPathFrom', () => {
  it('returns_the_page_the_user_asked_for_with_its_query_and_hash', () => {
    expect(returnPathFrom(from('/campaigns/abc'))).toBe('/campaigns/abc');
    expect(returnPathFrom(from('/members', '?page=2', '#top'))).toBe('/members?page=2#top');
  });

  it('falls_back_to_the_dashboard_when_there_is_nothing_to_return_to', () => {
    expect(returnPathFrom(undefined)).toBe('/');
    expect(returnPathFrom(null)).toBe('/');
    expect(returnPathFrom({})).toBe('/');
    expect(returnPathFrom('/members')).toBe('/');
    expect(returnPathFrom({ from: 'nope' })).toBe('/');
    expect(returnPathFrom({ from: { pathname: 42 } })).toBe('/');
  });

  it('never_returns_to_the_sign_in_page_itself', () => {
    expect(returnPathFrom(from('/login'))).toBe('/');
  });

  it('never_sends_the_user_to_another_origin', () => {
    expect(returnPathFrom(from('//evil.example/phish'))).toBe('/');
    expect(returnPathFrom(from('https://evil.example'))).toBe('/');
    expect(returnPathFrom(from('javascript:alert(1)'))).toBe('/');
  });
});
