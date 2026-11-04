import test from 'node:test';
import assert from 'node:assert/strict';
import { claudeDecision, codexApprovalReply, codexQuestionReply } from './protocol.mjs';

test('unknown Claude result denies permission, explicit click allows once', () => {
  assert.equal(claudeDecision('timeout').hookSpecificOutput.decision.behavior, 'deny');
  assert.deepEqual(claudeDecision('allow'), { hookSpecificOutput: { hookEventName: 'PermissionRequest', decision: { behavior: 'allow' } } });
});
test('Codex cannot accept an unavailable decision or extend approval to a session', () => {
  assert.deepEqual(codexApprovalReply(7, 'allow', ['decline']), { id: 7, result: { decision: 'decline' } });
  assert.deepEqual(codexApprovalReply(9, 'allow', ['accept','decline']), { id: 9, result: { decision: 'accept' } });
});
test('Codex question answers preserve identifiers and cancellation', () => {
  assert.deepEqual(codexQuestionReply('a', { branch: 'dev', another: 'deny' }), { id: 'a', result: { answers: { branch: { answers: ['dev'] }, another: { answers: [] } } } });
});
