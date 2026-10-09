export const MIN_PASSWORD_LENGTH = 8

export interface CredentialErrors {
  email?: string
  password?: string
}

export function validateCredentials(email: string, password: string): CredentialErrors {
  const errors: CredentialErrors = {}
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
    errors.email = 'Enter a valid email address.'
  }
  if (password.length < MIN_PASSWORD_LENGTH) {
    errors.password = `Password must be at least ${MIN_PASSWORD_LENGTH} characters.`
  }
  return errors
}
