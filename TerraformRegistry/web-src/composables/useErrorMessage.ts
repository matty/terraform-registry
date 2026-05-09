/**
 * Extracts a human-readable error message from an API error response.
 * Handles multiple response shapes:
 * - { error: "..." } — our standard error shape
 * - { detail: "..." } — ASP.NET ProblemDetails
 * - { errors: ["..."] } — validation errors
 * - { message: "..." } — generic
 * - { title: "..." } — ProblemDetails title
 */
export function extractErrorMessage(error: any, fallback: string): string {
  const data = error?.data

  if (data) {
    // Our API returns { error: "..." }
    if (typeof data.error === 'string') return data.error
    // ASP.NET ProblemDetails
    if (typeof data.detail === 'string') return data.detail
    // Generic message
    if (typeof data.message === 'string') return data.message
    // Validation errors array
    if (Array.isArray(data.errors) && data.errors.length > 0) return data.errors[0]
    // ProblemDetails title
    if (typeof data.title === 'string' && data.title !== 'Not Found') return data.title
  }

  // Network or unknown error
  if (typeof error?.message === 'string' && error.message !== 'fetch failed') return error.message

  return fallback
}
