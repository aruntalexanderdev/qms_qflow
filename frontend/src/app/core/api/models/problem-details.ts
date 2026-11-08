/** RFC 7807 problem details as returned by the API (ProblemDetailsSetup). `code` is the stable, translatable key. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  correlationId?: string;
  /** Present for `validation.failed`: field name (camelCase) to messages. */
  errors?: Record<string, string[]>;
}
