import { badgeVariantClass, type BadgeVariant } from './badgeStyles';

interface BadgeProps {
  children: string;
  variant?: BadgeVariant;
  className?: string;
}

export function Badge({ children, variant = 'default', className = '' }: BadgeProps) {
  return (
    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${badgeVariantClass[variant]} ${className}`}>
      {children}
    </span>
  );
}
