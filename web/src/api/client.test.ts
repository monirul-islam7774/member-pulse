import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, setAuthHandlers } from './client';
import { json, mockApi, problem } from '../test/http';

const onUnauthorized = vi.fn();

/** Awaits a call that must fail and hands back the ApiError it threw. */
async function failure(call: Promise<unknown>): Promise<ApiError> {
  try {
    await call;
  } catch (e) {
    return e as ApiError;
  }
  throw new Error('Expected the request to fail, but it succeeded.');
}

beforeEach(() => {
  onUnauthorized.mockReset();
  setAuthHandlers({ getToken: () => 'abc123', onUnauthorized });
});

describe('api client', () => {
  it('attaches_the_bearer_token', async () => {
    const { calls } = mockApi({ 'GET /api/members': json({ items: [] }) });

    await api.get('/api/members');

    expect(calls[0].headers.get('Authorization')).toBe('Bearer abc123');
  });

  it('sends_no_authorization_header_without_a_token', async () => {
    setAuthHandlers({ getToken: () => null, onUnauthorized });
    const { calls } = mockApi({ 'GET /api/members': json({}) });

    await api.get('/api/members');

    expect(calls[0].headers.has('Authorization')).toBe(false);
  });

  it('adds_query_parameters_and_skips_undefined_ones', async () => {
    const { calls } = mockApi({ 'GET /api/members': json({}) });

    await api.get('/api/members', { page: 2, pageSize: 20, search: undefined });

    expect(calls[0].path).toBe('/api/members?page=2&pageSize=20');
  });

  it('posts_json_with_a_content_type', async () => {
    const { calls } = mockApi({ 'POST /api/segments': json({ id: '1' }, 201) });

    await api.post('/api/segments', { name: 'Whales' });

    expect(calls[0].body).toEqual({ name: 'Whales' });
    expect(calls[0].headers.get('Content-Type')).toBe('application/json');
  });

  it('a_401_calls_onUnauthorized_and_throws_ApiError', async () => {
    mockApi({ 'GET /api/members': problem(401, 'Unauthorized') });

    await expect(api.get('/api/members')).rejects.toMatchObject({ status: 401 });

    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it('a_401_from_login_does_not_call_onUnauthorized', async () => {
    mockApi({ 'POST /api/auth/login': problem(401, 'Invalid credentials') });

    await expect(api.post('/api/auth/login', { email: 'a', password: 'b' })).rejects.toBeInstanceOf(ApiError);

    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('parses_problem_details_into_ApiError', async () => {
    mockApi({ 'POST /api/members': problem(409, 'Email already registered', "A member with email 'a@b.c' already exists.") });

    const error = await failure(api.post('/api/members', {}));

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(error.message).toBe("A member with email 'a@b.c' already exists.");
    expect(error.problem?.title).toBe('Email already registered');
  });

  it('falls_back_to_the_status_when_the_error_body_is_not_json', async () => {
    mockApi({ 'GET /api/members': new Response('<html>Bad gateway</html>', { status: 502 }) });

    const error = await failure(api.get('/api/members'));

    expect(error.status).toBe(502);
    expect(error.message).toBe('The server returned an error (502).');
  });

  it('a_network_failure_becomes_status_0', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await failure(api.get('/api/members'));

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(0);
  });

  it('204_resolves_undefined', async () => {
    mockApi({ 'POST /api/campaigns/1/pause': new Response(null, { status: 204 }) });

    await expect(api.post('/api/campaigns/1/pause')).resolves.toBeUndefined();
  });
});
