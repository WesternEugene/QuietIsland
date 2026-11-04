export function claudeDecision(decision) {
  return { hookSpecificOutput: { hookEventName: 'PermissionRequest', decision: {
    behavior: decision === 'allow' ? 'allow' : 'deny',
    ...(decision === 'allow' ? {} : { message: 'Запрос отклонён в Quiet Island или истекло время ожидания.' })
  } } };
}

export function codexApprovalReply(id, decision, available) {
  const canAccept = !Array.isArray(available) || available.includes('accept');
  return { id, result: { decision: decision === 'allow' && canAccept ? 'accept' : 'decline' } };
}

export function codexQuestionReply(id, answers) {
  return { id, result: { answers: Object.fromEntries(Object.entries(answers).map(([key, answer]) => [key, { answers: answer === 'deny' ? [] : [answer] }])) } };
}
